using Microsoft.UI.Xaml.Controls;
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
    public static async Task SyncAsync(string localPath, string remotePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(localPath) || string.IsNullOrWhiteSpace(remotePath)) return;

            var localDir = new DirectoryInfo(localPath);
            var remoteDir = new DirectoryInfo(remotePath);

            var localExists = localDir.Exists;
            var remoteExists = remoteDir.Exists;

            if (!localExists && !remoteExists) return;

            // 1. Determine Direction
            var localMax = localExists ? GetMaxLastWriteTime(localDir) : DateTime.MinValue;
            var remoteMax = remoteExists ? GetMaxLastWriteTime(remoteDir) : DateTime.MinValue;

            // Tolerance check for "Up to date" to avoid ping-pong syncs if times are very close
            if (Math.Abs((localMax - remoteMax).Ticks) < TicksThreshold)
            {
                // Consider equal
                Plugin.Instance?.Notify(InfoBarSeverity.Informational,
                    Plugin.GetLocalized("Ui_SyncUpToDate") ?? "Save is up to date.");
                return;
            }

            if (remoteMax > localMax)
            {
                // Source: Remote -> Target: Local
                if (!remoteExists) return; // Should not happen
                await SyncDifferentialAsync(remoteDir, localDir);

                Plugin.Instance?.Notify(InfoBarSeverity.Success,
                    Plugin.GetLocalized("Ui_SyncFromRemoteSuccess") ?? "Synced from remote.");
            }
            else
            {
                // Source: Local -> Target: Remote
                if (!localExists) return;
                await SyncDifferentialAsync(localDir, remoteDir);

                Plugin.Instance?.Notify(InfoBarSeverity.Success,
                    Plugin.GetLocalized("Ui_SyncToRemoteSuccess") ?? "Synced to remote.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Instance?.Notify(InfoBarSeverity.Error,
                string.Format(Plugin.GetLocalized("Ui_SyncError") ?? "Sync Error: {0}", ex.Message));
        }
    }

    private static DateTime GetMaxLastWriteTime(DirectoryInfo dir)
    {
        try
        {
            var files = dir.GetFiles("*", SearchOption.AllDirectories);
            if (files.Length == 0) return DateTime.MinValue;
            return files.Max(f => f.LastWriteTime);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Mirrors source directory to target directory efficiently.
    /// </summary>
    private static async Task SyncDifferentialAsync(DirectoryInfo source, DirectoryInfo target)
    {
        await Task.Run(() =>
        {
            if (!target.Exists) target.Create();

            // 1. Index Source Files (Relative Path -> FileInfo)
            var sourceFiles = source.GetFiles("*", SearchOption.AllDirectories);
            var sourceMap = new Dictionary<string, FileInfo>();

            foreach (var sFile in sourceFiles)
            {
                var relPath = Path.GetRelativePath(source.FullName, sFile.FullName);
                sourceMap[relPath] = sFile;
            }

            // 2. Index Target Files
            var targetFiles = target.GetFiles("*", SearchOption.AllDirectories);

            // 3. Delete Extraneous Files in Target
            foreach (var tFile in targetFiles)
            {
                var relPath = Path.GetRelativePath(target.FullName, tFile.FullName);
                if (!sourceMap.ContainsKey(relPath)) tFile.Delete();
            }

            // 4. Remove Empty Directories in Target (Bottom-up cleanup)
            // Note: This is optional, but keeps tree clean. 
            // skipping for simplicity or implementing if needed. 
            // A simpler approach for directories: ensure source dirs exist in target.

            // 5. Create Directories & Copy/Update Files
            foreach (var kvp in sourceMap)
            {
                var relPath = kvp.Key;
                var sFile = kvp.Value;

                var targetFullPath = Path.Combine(target.FullName, relPath);
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
                    // Compare Time (Allow 2s tolerance for FS differences)
                    else if (Math.Abs((sFile.LastWriteTime - tFile.LastWriteTime).Ticks) > TicksThreshold)
                        shouldCopy = true;
                }

                if (shouldCopy)
                {
                    if (tFile.Directory?.Exists == false) tFile.Directory.Create();
                    sFile.CopyTo(targetFullPath, true);
                }
            }

            // 6. Recursively delete empty directories in target that are not in source
            // Quick approach: Get all target dirs, if not in source, delete?
            // Safer: Just assume the file-based deletion handled content. 
            // If strict directory mirroring is required:
            DeleteEmptyDirs(target); // Cleanup
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
        if (directories.Count < 2) return; // Should not happen given initialization

        string? remoteRoot = null;
        string? relativeSuffix = null;

        string? requiredSettingName = null;
        string? requiredPath = null;

        // Logic to determine Remote Path
        if (displayPath.StartsWith("%GameRoot%", StringComparison.OrdinalIgnoreCase))
        {
            // Case 1: Game Root
            requiredSettingName = Plugin.GetLocalized("Ui_GameRoot") ?? "Game Root";
            var libraryRoot = directories[1].Path;

            if (string.IsNullOrWhiteSpace(libraryRoot))
            {
                Plugin.Instance.Notify(InfoBarSeverity.Warning,
                    Plugin.GetLocalized("Ui_SyncError") ?? "Sync Error",
                    string.Format(Plugin.GetLocalized("Ui_Error_SettingNotSet") ?? "Sync path for {0} is not set.",
                        requiredSettingName));
                return;
            }

            // Check if Base Path Exists
            if (!Directory.Exists(libraryRoot))
            {
                Plugin.Instance.Notify(InfoBarSeverity.Warning,
                    Plugin.GetLocalized("Ui_SyncError") ?? "Sync Error",
                    string.Format(Plugin.GetLocalized("Ui_Error_PathNotFound") ?? "Path for {0} not found: {1}",
                        requiredSettingName, libraryRoot));
                return;
            }

            // For Game Root, we must append the game's folder name to the remote library root
            // logical Mapping: LocalLibrary/GameFolder -> RemoteLibrary/GameFolder
#pragma warning disable CS0618 // 类型或成员已过时
            var gamePath = game.LocalPath ?? game.Path; // Fallback to compatible Obsolete property if LocalPath is null
#pragma warning restore CS0618 // 类型或成员已过时
            if (string.IsNullOrWhiteSpace(gamePath))
            {
                Plugin.Instance.Notify(InfoBarSeverity.Warning,
                   Plugin.GetLocalized("Ui_SyncError") ?? "Sync Error",
                   "Could not determine local game path.");
                return;
            }

            var gameFolderName = new DirectoryInfo(gamePath).Name;
            remoteRoot = Path.Combine(libraryRoot, gameFolderName);

            // Remove "%GameRoot%" length. 
            if (displayPath.Length > "%GameRoot%".Length)
                relativeSuffix = displayPath.Substring("%GameRoot%".Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            else
                relativeSuffix = string.Empty;
        }
        else if (displayPath.StartsWith("%"))
        {
            // Case 2: User Data (Documents, AppData, etc.)
            requiredSettingName = Plugin.GetLocalized("Ui_UserData") ?? "User Data";
            requiredPath = directories[0].Path;

            if (string.IsNullOrWhiteSpace(requiredPath))
            {
                Plugin.Instance.Notify(InfoBarSeverity.Warning,
                    Plugin.GetLocalized("Ui_SyncError") ?? "Sync Error",
                    string.Format(Plugin.GetLocalized("Ui_Error_SettingNotSet") ?? "Sync path for {0} is not set.",
                        requiredSettingName));
                return;
            }

            // Check if Base Path Exists
            if (!Directory.Exists(requiredPath))
            {
                Plugin.Instance.Notify(InfoBarSeverity.Warning,
                    Plugin.GetLocalized("Ui_SyncError") ?? "Sync Error",
                    string.Format(Plugin.GetLocalized("Ui_Error_PathNotFound") ?? "Path for {0} not found: {1}",
                        requiredSettingName, requiredPath));
                return;
            }

            // Find which token matches
            var closeIndex = displayPath.IndexOf('%', 1);
            if (closeIndex > 1)
            {
                var token = displayPath.Substring(0, closeIndex + 1); // e.g. %Documents%

                var relativeBase = GetRelativePathFromToken(token);

                remoteRoot = string.IsNullOrEmpty(relativeBase)
                    ? directories[0].Path
                    : Path.Combine(directories[0].Path, relativeBase);

                if (displayPath.Length > token.Length)
                    relativeSuffix = displayPath.Substring(token.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                else
                    relativeSuffix = string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(remoteRoot))
        {
            Plugin.Instance.Notify(InfoBarSeverity.Warning,
                Plugin.GetLocalized("Ui_SyncError") ?? "Sync Error",
                "Could not determine remote sync path for this game.");
            return;
        }

        try
        {
            var remoteFullPath = string.IsNullOrWhiteSpace(relativeSuffix)
                ? remoteRoot
                : Path.Combine(remoteRoot, relativeSuffix);

            await SyncAsync(localPath, remoteFullPath);
        }
        catch (Exception ex)
        {
            Plugin.Instance.Notify(InfoBarSeverity.Error,
                Plugin.GetLocalized("Ui_SyncError") ?? "Sync Error",
                ex.Message);
        }
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