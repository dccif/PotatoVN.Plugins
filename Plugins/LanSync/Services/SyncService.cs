using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.PluginBase.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase.Services;

public static class SyncService
{
    private const long TicksThreshold = 2 * 1000 * 10000; // 2 seconds tolerance for file time comparison

    public class SyncOperationResult
    {
        public bool Success { get; set; }
        public bool Skipped { get; set; }
        public bool IsUpload { get; set; } // true: Local -> Remote
        public string RemoteName { get; set; } = string.Empty;
        public string RemotePath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Syncs two folders using a "Latest Modified Wins" strategy with differential file copying.
    /// Logic:
    /// 1. Identify which folder (Local or Remote) contains the most recently modified file.
    /// 2. Designate that folder as 'Source' and the other as 'Target'.
    /// 3. Mirror Source to Target:
    ///    - Delete files in Target not present in Source.
    ///    - Copy files from Source to Target only if they changed (Size or LastWriteTime differs).
    /// Performs synchronization against a specific remote using a pre-computed local state.
    /// </summary>
    private static async Task<SyncOperationResult> SyncAsync(DirectoryInfo localDir, DirectoryState localState,
        string remotePath, string? remoteName)
    {
        var result = new SyncOperationResult
        {
            RemoteName = remoteName ?? "Unknown",
            RemotePath = remotePath
        };

        try
        {
            if (string.IsNullOrWhiteSpace(remotePath))
            {
                result.Success = false;
                result.ErrorMessage = "Invalid remote path";
                return result;
            }

            var remoteDir = new DirectoryInfo(remotePath);
            var remoteState = await GetDirectoryStateAsync(remoteDir);

            if (!localState.Exists && !remoteState.Exists)
            {
                result.Skipped = true;
                result.Success = true;
                return result;
            }

            // Tolerance check for "Up to date"
            if (Math.Abs((localState.MaxWriteTime - remoteState.MaxWriteTime).Ticks) < TicksThreshold)
            {
                result.Skipped = true;
                result.Success = true;
                return result;
            }

            // Determine Direction
            if (remoteState.MaxWriteTime > localState.MaxWriteTime)
            {
                // Sync Remote -> Local
                if (!remoteState.Exists)
                {
                    result.Skipped = true;
                    result.Success = true;
                    return result;
                }

                await SyncDifferentialAsync(remoteDir, remoteState.Files, localDir, localState.Files);
                result.IsUpload = false;
                result.Success = true;
            }
            else
            {
                // Sync Local -> Remote
                if (!localState.Exists)
                {
                    result.Skipped = true;
                    result.Success = true;
                    return result;
                }

                await SyncDifferentialAsync(localDir, localState.Files, remoteDir, remoteState.Files);
                result.IsUpload = true;
                result.Success = true;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private class DirectoryState
    {
        public bool Exists { get; set; }
        public DateTime MaxWriteTime { get; set; }
        public FileInfo[] Files { get; set; } = [];
    }

    private static Task<DirectoryState> GetDirectoryStateAsync(DirectoryInfo dir)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!dir.Exists) return new DirectoryState { Exists = false, MaxWriteTime = DateTime.MinValue };

                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip =
                        FileAttributes.ReparsePoint | FileAttributes.System // Skip symlinks/system files for safety
                };

                // Use EnumerateFiles heavily optimized by OS for bulk retrieval, but safe via options
                // Note: ToList() helps freeze the state, but we need array for index access later
                var files = dir.GetFiles("*", options);
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
    private static async Task SyncDifferentialAsync(DirectoryInfo sourceDir, FileInfo[] sourceFiles,
        DirectoryInfo targetDir, FileInfo[] targetFiles)
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

            // 2. Index Target Files (Relative Path -> FileInfo)
            var targetMap = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in targetFiles)
            {
                var relPath = Path.GetRelativePath(targetDir.FullName, f.FullName);
                targetMap[relPath] = f;
            }

            // 3. Pre-create required directories in target (Parallel)
            // Optimization: Only create directories that are NOT implied by existing target files.

            // Get all unique directories needed for source files
            var sourceDirs = sourceMap.Keys
                .Select(k => Path.GetDirectoryName(k))
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => d!)
                .Distinct()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Get all unique directories that already exist (implied by target files)
            var targetDirs = targetMap.Keys
                .Select(k => Path.GetDirectoryName(k))
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => d!)
                .Distinct();

            // Remove directories that already "exist" in our target map concept
            sourceDirs.ExceptWith(targetDirs);

            // Create only the missing ones
            Parallel.ForEach(sourceDirs, dirRel =>
            {
                try
                {
                    Directory.CreateDirectory(Path.Combine(targetDir.FullName, dirRel));
                }
                catch
                {
                    // Ignore directory creation errors (e.g. race conditions)
                }
            });

            // 4. Delete Extraneous Files in Target (Parallel)
            Parallel.ForEach(targetMap, kvp =>
            {
                if (!sourceMap.ContainsKey(kvp.Key))
                    try
                    {
                        kvp.Value.Delete();
                    }
                    catch
                    {
                        // Ignore delete errors
                    }
            });

            // 5. Copy/Update Files (Parallel)
            Parallel.ForEach(sourceMap, kvp =>
            {
                var relPath = kvp.Key;
                var sFile = kvp.Value;

                var targetFullPath = Path.Combine(targetDir.FullName, relPath);

                var shouldCopy = false;

                if (targetMap.TryGetValue(relPath, out var tFile))
                {
                    // Use cached metadata
                    if (sFile.Length != tFile.Length)
                        shouldCopy = true;
                    // Compare Time (Allow 2s tolerance)
                    else if (Math.Abs((sFile.LastWriteTime - tFile.LastWriteTime).Ticks) > TicksThreshold)
                        shouldCopy = true;
                }
                else
                {
                    shouldCopy = true;
                }

                if (shouldCopy)
                    try
                    {
                        // Directory is already guaranteed to exist from Step 3
                        sFile.CopyTo(targetFullPath, true);
                    }
                    catch (Exception copyEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LanSync] Copy Error: {copyEx.Message}");
                    }
            });

            // 6. Cleanup Empty Directories
            DeleteEmptyDirs(targetDir);
        });
    }

    private static void DeleteEmptyDirs(DirectoryInfo dir)
    {
        try
        {
            foreach (var d in dir.EnumerateDirectories()) DeleteEmptyDirs(d);

            // Optimization: Efficiently check for emptiness without allocating arrays
            if (!dir.EnumerateFileSystemInfos().Any()) dir.Delete();
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

        // Optimization: Scan local directory ONCE for all potential remotes
        var localDir = new DirectoryInfo(localPath);
        var localState = await GetDirectoryStateAsync(localDir);

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
                var libraryRoots = directories
                    .Where(d => d.Type == SyncDirectoryType.Library && !string.IsNullOrWhiteSpace(d.Path)).ToList();

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
                                return (fullUrl, root.Name);
                            }
                        }
                        catch
                        {
                        }

                        return null as (string Path, string Name)?;
                    });
                }));

                targetRemotePaths.AddRange(validPaths.Where(x => x.HasValue).Select(x => x!.Value));
            }
        }
        else if (displayPath.StartsWith('%'))
        {
            // Case 2: User Data (Documents, AppData, etc.)
            requiredSettingName = Plugin.GetLocalized("Ui_UserData") ?? "User Data";

            var userDataDirs = directories
                .Where(d => d.Type == SyncDirectoryType.User && !string.IsNullOrWhiteSpace(d.Path)).ToList();
            if (userDataDirs.Count != 0)
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

                        var fullPath = string.IsNullOrWhiteSpace(relativeSuffix)
                            ? remoteRoot
                            : Path.Combine(remoteRoot, relativeSuffix);
                        targetRemotePaths.Add((fullPath, userDir.Name));
                    }
                }
            }
        }

        if (targetRemotePaths.Count == 0)
        {
            if (requiredSettingName != null)
                Plugin.Instance.Notify(InfoBarSeverity.Warning,
                    Plugin.GetLocalized("Ui_SyncError") ?? "Sync Error",
                    "Could not determine any valid remote sync path for this game.");
            return;
        }

        // Run syncs in parallel using the PRE-SCANNED local state
        var results = await Task.WhenAll(targetRemotePaths.Select(async target =>
        {
            return await SyncAsync(localDir, localState, target.Path, target.Name);
        }));

        var summaryBuilder = new StringBuilder();
        var anyError = false;
        var hasContent = false;

        var localString = Plugin.GetLocalized("Ui_Local") ?? "Local";

        foreach (var res in results)
        {
            var emoji = res.Success ? "✅" : "❌";
            var arrow = res.Skipped ? "=" : res.IsUpload ? "->" : "<-";

            // Format: [Emoji] [LocalLocalized] [Arrow] [RemoteName] ([RemotePath])
            summaryBuilder.AppendLine($"{emoji} {localString} {arrow} {res.RemoteName} ({res.RemotePath})");

            if (!res.Success) anyError = true;
            hasContent = true;
        }

        if (hasContent)
        {
            // Remove last newline
            var summary = summaryBuilder.ToString().TrimEnd();

            Plugin.Instance.Notify(
                anyError ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                Plugin.GetLocalized("Ui_SyncTaskComplete") ?? "Sync Complete",
                summary);
        }
    }

    private static string GetRelativeSuffix(string displayPath, string rootToken)
    {
        if (displayPath.Length > rootToken.Length)
            return displayPath[rootToken.Length..]
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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