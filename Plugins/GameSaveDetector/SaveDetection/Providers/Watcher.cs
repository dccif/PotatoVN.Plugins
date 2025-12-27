using GalgameManager.Models;
using PotatoVN.App.PluginBase.SaveDetection.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase.SaveDetection.Providers;

internal class WatcherProvider : ISaveCandidateProvider
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly List<string> _candidatePaths = new();
    private readonly HashSet<string> _pendingPaths = new();
    private bool _isMonitoring;

    public Task StartAsync(DetectionContext context, Func<string, IoOperation, bool> pathFilter)
    {
        if (context.Game == null)
        {
            context.Log("Game context is missing for FileSystemWatcherSaveProvider.", LogLevel.Warning);
            return Task.CompletedTask;
        }

        context.Log("Initializing candidate paths for FileSystemWatcher...", LogLevel.Debug);
        InitializeCandidatePaths(context.Game, context.Settings);

        StartMonitoring(context, pathFilter);

        if (_pendingPaths.Count > 0) _ = RetryMonitoringAsync(context, pathFilter);

        return Task.CompletedTask;
    }

    public void Stop()
    {
        _isMonitoring = false;
        foreach (var watcher in _watchers)
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch
            {
            }

        _watchers.Clear();
        _candidatePaths.Clear();
        _pendingPaths.Clear();
    }

    private void InitializeCandidatePaths(Galgame game, SaveDetectorOptions options)
    {
        _candidatePaths.Clear();
        _pendingPaths.Clear();

        // 1. Game Install Path
        if (!string.IsNullOrEmpty(game.LocalPath))
            AddCandidatePath(game.LocalPath);

        // 2. Standard User Paths (From centralized GenericRoots)
        foreach (var root in options.GenericRoots) AddCandidatePath(root);

        // 3. Heuristic Paths
        AddHeuristicPaths(game, options);
    }

    private void AddCandidatePath(string path)
    {
        if (!string.IsNullOrEmpty(path) && !_candidatePaths.Contains(path)) _candidatePaths.Add(path);
    }

    private void AddHeuristicPaths(Galgame game, SaveDetectorOptions options)
    {
        var keywords = ExtractGameKeywords(game, options);
        var basePaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        var currentAppPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

        foreach (var keyword in keywords)
        {
            if (string.IsNullOrEmpty(keyword)) continue;

            foreach (var basePath in basePaths)
            {
                var combinedPath = Path.Combine(basePath, keyword);
                if (!IsPathExcluded(combinedPath, currentAppPath, options)) AddCandidatePath(combinedPath);
            }
        }
    }

    private List<string> ExtractGameKeywords(Galgame game, SaveDetectorOptions options)
    {
        var keywords = new List<string>();
        if (game.Name?.Value is { } name) keywords.Add(name);
        if (!string.IsNullOrEmpty(game.ChineseName?.Value)) keywords.Add(game.ChineseName.Value);
        if (game.OriginalName?.Value is { } original) keywords.Add(original);
        if (!string.IsNullOrEmpty(game.Developer?.Value)) keywords.Add(game.Developer.Value);

        if (game.Categories != null)
            foreach (var category in game.Categories)
                if (category.Name != null)
                    keywords.Add(category.Name);

        return keywords.Distinct().ToList();
    }

    private bool IsPathExcluded(string path, string appPath, SaveDetectorOptions options, string? gameRoot = null)
    {
        if (string.IsNullOrEmpty(path)) return true;
        if (!string.IsNullOrEmpty(appPath) && path.StartsWith(appPath, StringComparison.OrdinalIgnoreCase)) return true;

        if (gameRoot != null && path.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        return Constants.ShouldExcludePath(path.AsSpan(), appPath);
    }

    private void StartMonitoring(DetectionContext context, Func<string, IoOperation, bool> pathFilter)
    {
        _isMonitoring = true;
        var currentAppPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        var gameRoot = context.Game?.LocalPath;

        if (gameRoot != null && Directory.Exists(gameRoot))
        {
            if (!string.IsNullOrEmpty(currentAppPath) &&
                gameRoot.StartsWith(currentAppPath, StringComparison.OrdinalIgnoreCase))
            {
                context.Log($"[Watcher] Skipping game root because it is inside app path: {gameRoot}", LogLevel.Debug);
            }
            else
            {
                context.Log($"[Watcher] Explicitly watching game root: {gameRoot}", LogLevel.Debug);
                CreateFileSystemWatcher(gameRoot, context, pathFilter);
            }
        }

        foreach (var path in _candidatePaths)
        {
            if (_watchers.Any(w => w.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;

            if (IsPathExcluded(path, currentAppPath, context.Settings, gameRoot))
            {
                context.Log($"[Watcher] Skipping excluded path: {path}", LogLevel.Debug);
                continue;
            }

            if (Directory.Exists(path))
            {
                context.Log($"[Watcher] Starting watch on: {path}", LogLevel.Debug);
                CreateFileSystemWatcher(path, context, pathFilter);
            }
            else
            {
                context.Log($"[Watcher] Path does not exist, adding to pending: {path}", LogLevel.Debug);
                _pendingPaths.Add(path);
            }
        }
    }

    private async Task RetryMonitoringAsync(DetectionContext context, Func<string, IoOperation, bool> pathFilter)
    {
        var gameRoot = context.Game?.LocalPath;
        for (var i = 0; i < context.Settings.WatcherRetryCount; i++)
        {
            await Task.Delay(context.Settings.WatcherRetryIntervalMs, context.Token);
            if (!_isMonitoring || context.Token.IsCancellationRequested || context.TargetProcess.HasExited) return;

            var successfullyAdded = new List<string>();
            foreach (var path in _pendingPaths)
                if (Directory.Exists(path))
                {
                    context.Log($"[Watcher] [Retry {i + 1}] Path appeared, starting watch: {path}", LogLevel.Debug);
                    CreateFileSystemWatcher(path, context, pathFilter);
                    successfullyAdded.Add(path);
                }

            foreach (var path in successfullyAdded) _pendingPaths.Remove(path);
            if (_pendingPaths.Count == 0) break;
        }

        if (_pendingPaths.Count > 0)
            context.Log($"[Watcher] Stopped retrying. {_pendingPaths.Count} paths still missing.", LogLevel.Debug);
    }

    private void CreateFileSystemWatcher(string path, DetectionContext context,
        Func<string, IoOperation, bool> pathFilter)
    {
        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                InternalBufferSize = 65536,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size |
                               NotifyFilters.Attributes | NotifyFilters.CreationTime
            };

            FileSystemEventHandler handler = (s, e) =>
            {
                if (!_isMonitoring) return;

                var op = e.ChangeType switch
                {
                    WatcherChangeTypes.Created => IoOperation.Create,
                    WatcherChangeTypes.Changed => IoOperation.Write,
                    WatcherChangeTypes.Renamed => IoOperation.Rename,
                    _ => IoOperation.Unknown
                };

                context.Log($"[Watcher] File Event: {e.FullPath} ({e.ChangeType} -> {op})", LogLevel.Debug);

                if (pathFilter(e.FullPath, op))
                {
                    context.Candidates.Enqueue(new PathCandidate(e.FullPath, ProviderSource.FileSystemWatcher,
                        DateTime.Now, op));
                    context.Log($"[Watcher] Candidate Added: {e.FullPath}", LogLevel.Debug);
                }
                else
                {
                    context.Log($"[Watcher] Filtered out: {e.FullPath}", LogLevel.Debug);
                }
            };
            watcher.Created += handler;
            watcher.Changed += handler;
            watcher.Renamed += (s, e) => handler(s, e);

            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            context.Log($"Failed to watch {path}: {ex.Message}", LogLevel.Warning);
        }
    }
}