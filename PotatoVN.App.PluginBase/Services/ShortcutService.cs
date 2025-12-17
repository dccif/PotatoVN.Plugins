using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PotatoVN.App.PluginBase.Helper;
using PotatoVN.App.PluginBase.Models;

namespace PotatoVN.App.PluginBase.Services;

public static class ShortcutService
{
    private record GamePaths(
        string ShortcutPath,      // .url Path (Desktop)
        string VbsPath,           // .vbs Path (Desktop)
        string LocalIconPath,     // .ico Path (Host Images Folder)
        string SunshineIconPath,  // .png Path (Pictures/sunshine)
        string UuidUri            // potato-vn://start/{uuid}
    );

    private static async Task<GamePaths?> PrepareAssetsAsync(object game, bool requireSunshineAssets = false)
    {
        var type = game.GetType();
        var uuidObj = type.GetProperty("Uuid")?.GetValue(game);
        // Name is LockableProperty<string>, need to get Value
        var nameProp = type.GetProperty("Name")?.GetValue(game);
        string? name = null;
        if (nameProp != null)
        {
            var valProp = nameProp.GetType().GetProperty("Value");
            name = valProp?.GetValue(nameProp) as string;
        }

        var exePath = type.GetProperty("ExePath")?.GetValue(game) as string;
        var localPath = type.GetProperty("LocalPath")?.GetValue(game) as string;

        if (uuidObj is not Guid uuid || string.IsNullOrEmpty(name)) return null;

        // 1. Basic Paths
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        var originalSafeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var safeBaseName = $"PotatoVN_{uuid}";

        var shortcutPath = Path.Combine(desktopPath, $"{originalSafeName}.url");
        var vbsPath = Path.Combine(desktopPath, $"{safeBaseName}.vbs");

        // Resolve Game Exe Path
        var gameExePath = exePath;
        if (!string.IsNullOrEmpty(gameExePath) && !Path.IsPathRooted(gameExePath) && !string.IsNullOrEmpty(localPath))
        {
            gameExePath = Path.Combine(localPath, gameExePath);
        }

        // 2. Prepare .ico Icon (For Desktop Shortcut)
        var localImagesFolder = await HostFileHelper.GetImageFolderPathAsync();
        if (string.IsNullOrEmpty(localImagesFolder))
        {
            localImagesFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PotatoVN", "ShortcutIcons");
            Directory.CreateDirectory(localImagesFolder);
        }

        var localIconPath = Path.Combine(localImagesFolder, $"{originalSafeName}.ico");

        if (!File.Exists(localIconPath) && !string.IsNullOrEmpty(gameExePath) && File.Exists(gameExePath))
        {
            await IconHelper.ExtractBestIconAsync(gameExePath, localIconPath);
        }

        // 3. Prepare .png Icon (For Sunshine)
        string sunshineIconPath = string.Empty;
        if (requireSunshineAssets)
        {
            var myPictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var sunshineDir = Path.Combine(myPictures, "sunshine");
            if (!Directory.Exists(sunshineDir)) Directory.CreateDirectory(sunshineDir);

            sunshineIconPath = Path.Combine(sunshineDir, $"{uuid}.png");

            if (!File.Exists(sunshineIconPath) && !string.IsNullOrEmpty(gameExePath) && File.Exists(gameExePath))
            {
                await IconHelper.SaveBestIconAsPngAsync(gameExePath, sunshineIconPath);
            }
        }

        return new GamePaths(shortcutPath, vbsPath, localIconPath, sunshineIconPath, $"potato-vn://start/{uuid}");
    }

        public static async Task CreateDesktopShortcut(object game)
        {
            try
            {
                var paths = await PrepareAssetsAsync(game, requireSunshineAssets: false);
                if (paths == null) return;

                await Task.Run(() =>
                {
                    var shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType != null)
                    {
                        dynamic shell = Activator.CreateInstance(shellType)!;
                        dynamic shortcut = shell.CreateShortcut(paths.ShortcutPath);

                        shortcut.TargetPath = paths.UuidUri;
                        
                        if (File.Exists(paths.LocalIconPath))
                        {
                            shortcut.IconLocation = $"{paths.LocalIconPath},0";
                        }
                        else
                        {
                             var appExePath = Process.GetCurrentProcess().MainModule?.FileName;
                             if(!string.IsNullOrEmpty(appExePath))
                                 shortcut.IconLocation = $"{appExePath},0";
                        }
                        
                        shortcut.Save();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CreateDesktopShortcut Error: {ex}");
            }
        }

    public static async Task ExportToSunshine(object game)
    {
        try
        {
            var paths = await PrepareAssetsAsync(game, requireSunshineAssets: true);
            if (paths == null) return;

            await GenerateSilentVbsAsync(game, paths);

            const string sunshineConfigPath = @"C:\Program Files\Sunshine\config\apps.json";
            if (!File.Exists(sunshineConfigPath))
            {
                Debug.WriteLine("Sunshine config not found.");
                return;
            }

            string jsonContent = await File.ReadAllTextAsync(sunshineConfigPath);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var config = JsonSerializer.Deserialize<SunshineConfig>(jsonContent, options) ?? new SunshineConfig();
            if (config.Apps == null) config.Apps = new List<SunshineApp>();

            var type = game.GetType();
            var nameProp = type.GetProperty("Name")?.GetValue(game);
            string targetAppName = "Unknown Game";
            if (nameProp != null)
            {
                var val = nameProp.GetType().GetProperty("Value")?.GetValue(nameProp) as string;
                if (!string.IsNullOrEmpty(val)) targetAppName = val;
            }

            var cmdString = $"wscript \"{paths.VbsPath}\" ";

            var appEntry = config.Apps.FirstOrDefault(a => a.Name == targetAppName);

            if (appEntry != null)
            {
                appEntry.Cmd = cmdString;
                appEntry.ImagePath = paths.SunshineIconPath;
                appEntry.AutoDetach = "true";
                appEntry.WaitAll = "true";
            }
            else
            {
                appEntry = new SunshineApp
                {
                    Name = targetAppName,
                    Cmd = cmdString,
                    ImagePath = paths.SunshineIconPath,
                    AutoDetach = "true",
                    WaitAll = "true",
                    ExitTimeout = "5"
                };
                config.Apps.Add(appEntry);
            }

            var newJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });

            try
            {
                await File.WriteAllTextAsync(sunshineConfigPath, newJson);
            }
            catch (UnauthorizedAccessException)
            {
                await WriteFileElevatedAsync(newJson, sunshineConfigPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ExportToSunshine Error: {ex}");
        }
    }

    private static async Task GenerateSilentVbsAsync(object game, GamePaths paths)
    {
        var type = game.GetType();
        var processName = type.GetProperty("ProcessName")?.GetValue(game) as string;
        var exePath = type.GetProperty("ExePath")?.GetValue(game) as string;

        if (string.IsNullOrEmpty(processName) && !string.IsNullOrEmpty(exePath))
        {
            processName = Path.GetFileName(exePath);
        }

        var vbsContent = new StringBuilder();
        vbsContent.AppendLine("Option Explicit");
        vbsContent.AppendLine("Dim WshShell, strCommand, strProcessName, objWMIService, colProcessList");
        vbsContent.AppendLine("Set WshShell = CreateObject(\"WScript.Shell\")");

        // Optimized: Launch URI directly using WshShell.Run
        // The URI is passed as a string. Double quotes are doubled in VBS.
        // Result VBS: WshShell.Run "potato-vn://start/...", 1, False
        vbsContent.AppendLine($"strCommand = \"{paths.UuidUri}\"");
        vbsContent.AppendLine("WshShell.Run strCommand, 1, False");

        // Process Monitoring (Essential for Sunshine to keep stream open)
        if (!string.IsNullOrEmpty(processName))
        {
            vbsContent.AppendLine($"strProcessName = \"{processName}\"");
            vbsContent.AppendLine("Set objWMIService = GetObject(\"winmgmts:\\\\.\\\\.\\\\root\\\\cimv2\")");
            vbsContent.AppendLine("WScript.Sleep 5000"); // 5s buffer for game to start
            vbsContent.AppendLine("Do");
            // Correctly escaped VBS query string
            vbsContent.AppendLine("    Set colProcessList = objWMIService.ExecQuery(\"Select * from Win32_Process Where Name = '\" & strProcessName & '\"\")");
            vbsContent.AppendLine("    If colProcessList.Count = 0 Then Exit Do");
            vbsContent.AppendLine("    WScript.Sleep 2000");
            vbsContent.AppendLine("Loop");
        }

        vbsContent.AppendLine("Set WshShell = Nothing");

        await File.WriteAllTextAsync(paths.VbsPath, vbsContent.ToString(), Encoding.Default);
    }

    private static async Task WriteFileElevatedAsync(string content, string destinationPath)
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8);

            // Corrected quote escaping
            var cmdArgs = $"/c copy /y \"{tempPath}\" \"{destinationPath}\"";
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArgs,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };

            await Task.Run(() =>
            {
                var process = Process.Start(startInfo);
                process?.WaitForExit();
                if (process != null && process.ExitCode != 0)
                {
                    throw new Exception($"Elevated copy failed with code: {process.ExitCode}");
                }
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Cancelled
        }
        finally
        {
            if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
        }
    }
}
