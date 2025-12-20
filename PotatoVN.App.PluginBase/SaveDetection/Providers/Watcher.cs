using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalgameManager.Models;
using PotatoVN.App.PluginBase.SaveDetection.Models;

namespace PotatoVN.App.PluginBase.SaveDetection.Providers;

internal class WatcherProvider : ISaveCandidateProvider
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly List<string> _candidatePaths = new();
    private bool _isMonitoring;

    public Task StartAsync(DetectionContext context, Func<string, bool> pathFilter)
    {
        if (context.Game == null)
        {
            context.Log("Game context is missing for FileSystemWatcherSaveProvider.", LogLevel.Warning);
            return Task.CompletedTask;
        }

        InitializeCandidatePaths(context.Game, context.Settings);

        StartMonitoring(context, pathFilter);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _isMonitoring = false;
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch { }
        }
        _watchers.Clear();
        _candidatePaths.Clear();
    }

    private void InitializeCandidatePaths(Galgame game, SaveDetectorOptions options)
    {
        _candidatePaths.Clear();

        // 1. Game Install Path
        if (!string.IsNullOrEmpty(game.LocalPath))
            AddCandidatePath(game.LocalPath);

        // 2. Standard User Paths
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        AddCandidatePath(documentsPath);
        AddCandidatePath(Path.Combine(documentsPath, "My Games"));
        AddCandidatePath(Path.Combine(documentsPath, "Saved Games"));

        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddCandidatePath(userProfilePath);
        AddCandidatePath(Path.Combine(userProfilePath, "Saved Games"));

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var localLowPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).Replace("Local", "LocalLow");

        AddCandidatePath(appDataPath);
        AddCandidatePath(localAppDataPath);
        AddCandidatePath(localLowPath);

        // 3. Heuristic Paths
        AddHeuristicPaths(game, options);
    }

    private void AddCandidatePath(string path)
    {
        if (!string.IsNullOrEmpty(path) && !_candidatePaths.Contains(path))
        {
            _candidatePaths.Add(path);
        }
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
                if (!IsPathExcluded(combinedPath, currentAppPath, options))
                {
                    AddCandidatePath(combinedPath);
                }
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
        {
            foreach (var category in game.Categories)
            {
                if (category.Name != null) keywords.Add(category.Name);
            }
        }

        // Note: Variant generation is now mainly in VotingAnalyzer
        // But we still need some basic keywords for path generation

        return keywords.Distinct().ToList();
    }

    private bool IsPathExcluded(string path, string appPath, SaveDetectorOptions options)
    {
        if (string.IsNullOrEmpty(path)) return true;
        if (!string.IsNullOrEmpty(appPath) && path.StartsWith(appPath, StringComparison.OrdinalIgnoreCase)) return true;

        return options.PathBlacklist.Any(b => path.Contains(b, StringComparison.OrdinalIgnoreCase));
    }

    private void StartMonitoring(DetectionContext context, Func<string, bool> pathFilter)
    {
        _isMonitoring = true;
        var currentAppPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

        foreach (var path in _candidatePaths)
        {
            if (IsPathExcluded(path, currentAppPath, context.Settings))
            {
                continue;
            }

            if (Directory.Exists(path))
            {
                CreateFileSystemWatcher(path, context, pathFilter);
            }
        }
    }

    private void CreateFileSystemWatcher(string path, DetectionContext context, Func<string, bool> pathFilter)
    {
        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                InternalBufferSize = 4096,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.CreationTime
            };

            FileSystemEventHandler handler = (s, e) =>
            {
                if (!_isMonitoring) return;
                if (pathFilter(e.FullPath))
                {
                    context.Candidates.Enqueue(new PathCandidate(e.FullPath, ProviderSource.FileSystemWatcher, DateTime.Now));
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