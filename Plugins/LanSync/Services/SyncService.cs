using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.PluginBase.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase.Services;

public static class SyncService
{
    private const long TicksThreshold = 2 * 1000 * 10000; // 2 seconds tolerance for file time comparison

    /// <summary>
    /// Syncs two folders using a "Latest Modified Wins" strategy with differential file copying.
    /// Logic:
    /// 1. Identify which folder (Local or Remote) contains the most recently modified file.
    /// 2. Designate that folder as 'Source' and the other as 'Target'.
    /// 3. Mirror Source to Target:
    ///    - Delete files in Target not present in Source.
    ///    - Copy files from Source to Target only if they changed (Size or LastWriteTime differs).
    /// </summary>
    /// <param name="localPath">Absolute path for local folder</param>
    /// <param name="remotePath">Absolute path for remote folder</param>
    /// <param name="remoteName">Optional name of remote for logging</param>
    public static async Task SyncAsync(string localPath, string remotePath, string? remoteName = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(localPath) || string.IsNullOrWhiteSpace(remotePath)) return;

            var localDir = new DirectoryInfo(localPath);
            var remoteDir = new DirectoryInfo(remotePath);

            // 1. Scan both directories in parallel to get state (MaxTime + FileList)
            var localTask = GetDirectoryStateAsync(localDir);
            var remoteTask = GetDirectoryStateAsync(remoteDir);

            await Task.WhenAll(localTask, remoteTask);

            var localState = localTask.Result;
            var remoteState = remoteTask.Result;

            if (!localState.Exists && !remoteState.Exists) return;

            // 2. Tolerance check for "Up to date"
            if (Math.Abs((localState.MaxWriteTime - remoteState.MaxWriteTime).Ticks) < TicksThreshold)
            {
                Plugin.Instance?.Notify(InfoBarSeverity.Informational,
                    Plugin.GetLocalized("Ui_SyncUpToDate") ?? "Save is up to date.");
                return;
            }

            // 3. Determine Direction
            if (remoteState.MaxWriteTime > localState.MaxWriteTime)
            {
                // Source: Remote -> Target: Local
                if (!remoteState.Exists) return;
                await SyncDifferentialAsync(remoteDir, remoteState.Files, localDir, localState.Files);

                var msg = !string.IsNullOrEmpty(remoteName)
                    ? string.Format(Plugin.GetLocalized("Ui_SyncFromRemoteFormat") ?? "{0} -> Local", remoteName)
                    : Plugin.GetLocalized("Ui_SyncFromRemoteSuccess") ?? "Synced from remote.";

                Plugin.Instance?.Notify(InfoBarSeverity.Success, msg);
            }
            else
            {
                // Source: Local -> Target: Remote
                if (!localState.Exists) return;
                await SyncDifferentialAsync(localDir, localState.Files, remoteDir, remoteState.Files);

                var msg = !string.IsNullOrEmpty(remoteName)
                    ? string.Format(Plugin.GetLocalized("Ui_SyncToRemoteFormat") ?? "Local -> {0}", remoteName)
                    : Plugin.GetLocalized("Ui_SyncToRemoteSuccess") ?? "Synced to remote.";

                Plugin.Instance?.Notify(InfoBarSeverity.Success, msg);
            }
        }
        catch (Exception ex)
        {
            Plugin.Instance?.Notify(InfoBarSeverity.Error,
                string.Format(Plugin.GetLocalized("Ui_SyncError") ?? "Sync Error: {0}", ex.Message));
        }
    }

    private class DirectoryState
    {
        public bool Exists { get; set; }
        public DateTime MaxWriteTime { get; set; }
        public FileInfo[] Files { get; set; } = Array.Empty<FileInfo>();
    }

    private static Task<DirectoryState> GetDirectoryStateAsync(DirectoryInfo dir)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!dir.Exists)
                {
                    return new DirectoryState { Exists = false, MaxWriteTime = DateTime.MinValue };
                }

                var files = dir.GetFiles("*", SearchOption.AllDirectories);
                var maxTime = files.Length > 0 ? files.Max(f => f.LastWriteTime) : DateTime.MinValue;

                return new DirectoryState
                {
                    Exists = true,
                    MaxWriteTime = maxTime,
                    Files = files
                };
            }
            catch
            {
                return new DirectoryState { Exists = false, MaxWriteTime = DateTime.MinValue };
            }
        });
    }

    /// <summary>
    /// Mirrors source directory to target directory efficiently using cached file lists.
    /// </summary>
    private static async Task SyncDifferentialAsync(DirectoryInfo sourceDir, FileInfo[] sourceFiles, DirectoryInfo targetDir, FileInfo[] targetFiles)
    {
        await Task.Run(() =>
        {
            if (!targetDir.Exists) targetDir.Create();

            // 1. Index Source Files (Relative Path -> FileInfo)
            var sourceMap = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in sourceFiles)
            {
                var relPath = Path.GetRelativePath(sourceDir.FullName, f.FullName);
                sourceMap[relPath] = f;
            }

            // 2. Delete Extraneous Files in Target (Parallel)
            var targetList = targetFiles.Select(f => new { File = f, RelPath = Path.GetRelativePath(targetDir.FullName, f.FullName) }).ToList();

            Parallel.ForEach(targetList, item =>
            {
                if (!sourceMap.ContainsKey(item.RelPath))
                {
                    try { item.File.Delete(); } catch { }
                }
            });

            // 3. Create Directories & Copy/Update Files (Parallel)
            Parallel.ForEach(sourceMap, kvp =>
            {
                var relPath = kvp.Key;
                var sFile = kvp.Value;

                var targetFullPath = Path.Combine(targetDir.FullName, relPath);
                var tFile = new FileInfo(targetFullPath);

                var shouldCopy = false;

                if (!tFile.Exists)
                {
                    shouldCopy = true;
                }
                else
                {
                    // Compare Size
                    if (sFile.Length != tFile.Length)
                        shouldCopy = true;
                    // Compare Time (Allow 2s tolerance)
                    else if (Math.Abs((sFile.LastWriteTime - tFile.LastWriteTime).Ticks) > TicksThreshold)
                        shouldCopy = true;
                }

                if (shouldCopy)
                {
                    try
                    {
                        if (tFile.Directory?.Exists == false) tFile.Directory.Create();
                        sFile.CopyTo(targetFullPath, true);
                    }
                    catch (Exception copyEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LanSync] Copy Error: {copyEx.Message}");
                    }
                }
            });

            // 4. Cleanup Empty Directories
            DeleteEmptyDirs(targetDir);
        });
    }

    private static void DeleteEmptyDirs(DirectoryInfo dir)
    {
        try
        {
            foreach (var d in dir.GetDirectories()) DeleteEmptyDirs(d);
            // If no files and no subdirs, delete
            if (dir.GetFiles().Length == 0 && dir.GetDirectories().Length == 0) dir.Delete();
        }
        catch
        {
            /* Ignore access errors */
        }
    }

    /// <summary>
    /// Orchestrates synchronization for a specific game.
    /// Determines the correct remote path based on the game's portable path type.
    /// </summary>
    public static async Task SyncGameAsync(GalgameManager.Models.Galgame game)
    {
        if (game?.DetectedSavePath is null) return;

        var localPath = game.DetectedSavePath?.ToPath();
        var displayPath = game.DetectedSavePath?.ToDisplay(); // e.g. %GameRoot%\Save

        if (string.IsNullOrWhiteSpace(localPath) || string.IsNullOrWhiteSpace(displayPath)) return;

        // Ensure Plugin Data is loaded
        if (Plugin.Instance == null) return;
        var directories = Plugin.Instance.Data.SyncDirectories;
        if (directories.Count == 0) return;

        var targetRemotePaths = new List<(string Path, string Name)>();
        string? requiredSettingName = null;

        // Logic to determine Remote Path
        if (displayPath.StartsWith("%GameRoot%", StringComparison.OrdinalIgnoreCase))
        {
            // Case 1: Game Root
            requiredSettingName = Plugin.GetLocalized("Ui_GameRoot") ?? "Game Root";

            // For Game Root, we must append the game's folder name to the remote library root
            // logical Mapping: LocalLibrary/GameFolder -> RemoteLibrary/GameFolder
#pragma warning disable CS0618
            var gamePath = game.LocalPath ?? game.Path;
#pragma warning restore CS0618

            if (!string.IsNullOrWhiteSpace(gamePath))
            {
                var gameFolderName = new DirectoryInfo(gamePath).Name;
                var relativeSuffix = GetRelativeSuffix(displayPath, "%GameRoot%");

                // Check all Library roots in parallel to find valid game directories
                var libraryRoots = directories.Where(d => d.Type == SyncDirectoryType.Library && !string.IsNullOrWhiteSpace(d.Path)).ToList();

                // We only sync to remote library roots that actually contain this game folder to avoid creating mess
                var validPaths = await Task.WhenAll(libraryRoots.Select(async root =>
                {
                    return await Task.Run(() =>
                    {
                        try
                        {
                            if (!Directory.Exists(root.Path)) return null;
                            var remoteGameRoot = Path.Combine(root.Path, gameFolderName);
                            if (Directory.Exists(remoteGameRoot))
                            {
                                var fullUrl = string.IsNullOrWhiteSpace(relativeSuffix)
                                    ? remoteGameRoot
                                    : Path.Combine(remoteGameRoot, relativeSuffix);
                                return (Path: fullUrl, Name: root.Name);
                            }
                        }
                        catch { }
                        return (null as (string Path, string Name)?);
                    });
                }));

                targetRemotePaths.AddRange(validPaths.Where(x => x.HasValue).Select(x => x!.Value));
            }
        }
        else if (displayPath.StartsWith("%"))
        {
            // Case 2: User Data (Documents, AppData, etc.)
            requiredSettingName = Plugin.GetLocalized("Ui_UserData") ?? "User Data";

            var userDataDirs = directories.Where(d => d.Type == SyncDirectoryType.User && !string.IsNullOrWhiteSpace(d.Path)).ToList();
            if (userDataDirs.Any())
            {
                var closeIndex = displayPath.IndexOf('%', 1);
                if (closeIndex > 1)
                {
                    var token = displayPath[..(closeIndex + 1)]; // e.g. %Documents%
                    var relativeBase = GetRelativePathFromToken(token);
                    var relativeSuffix = GetRelativeSuffix(displayPath, token);

                    foreach (var userDir in userDataDirs)
                    {
                        var remoteRoot = string.IsNullOrEmpty(relativeBase)
                             ? userDir.Path
                             : Path.Combine(userDir.Path, relativeBase);

                        var fullPath = string.IsNullOrWhiteSpace(relativeSuffix) ? remoteRoot : Path.Combine(remoteRoot, relativeSuffix);
                        targetRemotePaths.Add((fullPath, userDir.Name));
                    }
                }
            }
        }

        if (targetRemotePaths.Count == 0)
        {
            if (requiredSettingName != null)
            {
                // Only warn if we actually expected to find a path but didn't (and we had roots configured)
                // If the game just doesn't exist on remote library roots, silent fail might be better? 
                // But sticking to original logic of warning if logic fails.
                // Actually, for GameRoot, if we didn't find the game folder on any remote root, maybe it's cleaner to warn?
                // Current behavior: Warn if "Could not determine any valid remote sync path".
                Plugin.Instance.Notify(InfoBarSeverity.Warning,
                   Plugin.GetLocalized("Ui_SyncError") ?? "Sync Error",
                   "Could not determine any valid remote sync path for this game.");
            }
            return;
        }

        // Run syncs in parallel
        await Task.WhenAll(targetRemotePaths.Select(async target =>
        {
            try
            {
                await SyncAsync(localPath, target.Path, target.Name);
            }
            catch (Exception ex)
            {
                Plugin.Instance.Notify(InfoBarSeverity.Error,
                    Plugin.GetLocalized("Ui_SyncError") ?? "Sync Error",
                    ex.Message);
            }
        }));
    }

    private static string GetRelativeSuffix(string displayPath, string rootToken)
    {
        if (displayPath.Length > rootToken.Length)
            return displayPath[rootToken.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Empty;
    }

    private static string GetRelativePathFromToken(string token)
    {
        return token.ToUpperInvariant() switch
        {
            "%APPDATA%" => Path.Combine("AppData", "Roaming"),
            "%LOCALAPPDATA%" => Path.Combine("AppData", "Local"),
            "%LOCALLOW%" => Path.Combine("AppData", "LocalLow"),
            "%DOCUMENTS%" => "Documents",
            "%USERPROFILE%" => "",
            _ => token.Trim('%')
        };
    }
}