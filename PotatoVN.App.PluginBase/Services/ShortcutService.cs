using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Net.Http;
using Newtonsoft.Json;
using System.Threading.Tasks;
using PotatoVN.App.PluginBase.Helper;
using PotatoVN.App.PluginBase.Models;
using GalgameManager.WinApp.Base.Contracts;
using Microsoft.UI.Xaml.Controls;
using GalgameManager.Models;

namespace PotatoVN.App.PluginBase.Services;

public static class ShortcutService
{
    private record GamePaths(
        string ShortcutPath,      // .url Path (Desktop)
        string VbsPath,           // .vbs Path (Desktop)
        string LocalIconPath,     // .ico Path (Host Images Folder)
        string SunshineIconPath,  // .png Path (Pictures/sunshine)
        string UuidUri,           // potato-vn://start/{uuid}
        string Uuid               // {uuid}
    );

    private static async Task<GamePaths?> PrepareAssetsAsync(Galgame game, bool requireSunshineAssets = false)
    {
        var uuid = game.Uuid;
        var name = game.Name.Value;

        var exePath = game.ExePath;
        var localPath = game.LocalPath;

        if (string.IsNullOrEmpty(name)) return null;

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
        var localImagesFolder = await FileHelper.GetImageFolderPathAsync();
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

        return new GamePaths(shortcutPath, vbsPath, localIconPath, sunshineIconPath, $"potato-vn://start/{uuid}", uuid.ToString());
    }

    public static async Task CreateDesktopShortcut(Galgame game, IPotatoVnApi api)
    {
        try
        {
            // 1. 准备路径资源
            var paths = await PrepareAssetsAsync(game, requireSunshineAssets: false);
            if (paths == null) return;

            // 2. 确定图标路径
            string iconPath = paths.LocalIconPath;
            if (!File.Exists(iconPath))
            {
                var appExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(appExePath)) iconPath = appExePath;
            }

            // 3. 构建内容
            // 顺序很重要：Steam 风格通常把 [InternetShortcut] 放在核心位置
            var urlContent = new StringBuilder();

            urlContent.AppendLine("[InternetShortcut]");
            urlContent.AppendLine("IDList=");
            urlContent.AppendLine($"URL={paths.UuidUri}");

            if (!string.IsNullOrEmpty(iconPath))
            {
                urlContent.AppendLine("IconIndex=0");
                urlContent.AppendLine($"IconFile={iconPath}");
            }

            // Steam 专用的 Property Store 标记 (防止图标变白纸的关键)
            urlContent.AppendLine("");
            urlContent.AppendLine("[{000214A0-0000-0000-C000-000000000046}]");
            urlContent.AppendLine("Prop3=19,0");

            // 4. 关键修正：删除旧文件
            if (File.Exists(paths.ShortcutPath)) File.Delete(paths.ShortcutPath);

            // 5. 核心修改：使用 Encoding.Unicode (即 UTF-16 LE)
            // Windows 内部全是 UTF-16。
            // 使用这种编码写入，Windows 能直接识别 "魔法少女" 这样的路径，
            // 而不需要 [InternetShortcut.W] 这种复杂的 UTF-7 转码块。
            // 这也避免了 GBK 系统把 UTF-8 BOM 读成 "锘縖" 的问题。
            await File.WriteAllTextAsync(paths.ShortcutPath, urlContent.ToString(), Encoding.Unicode);
            api.Info(InfoBarSeverity.Success, msg: Plugin.GetLocalized("ShortcutCreated") ?? "Desktop shortcut created successfully");
        }
        catch (Exception ex)
        {
            api.Info(InfoBarSeverity.Error, msg: Plugin.GetLocalized("ShortcutCreateFailed") ?? "Failed to create desktop shortcut");
            Debug.WriteLine($"CreateDesktopShortcut Error: {ex}");
        }
    }

    public static async Task ExportToSunshine(Galgame game, IPotatoVnApi api)
    {
        try
        {
            var paths = await PrepareAssetsAsync(game, requireSunshineAssets: true);
            if (paths == null) return;

            const string sunshineConfigPath = @"C:\Program Files\Sunshine\config\apps.json";
            if (!File.Exists(sunshineConfigPath))
            {
                Debug.WriteLine("Sunshine config not found.");
                api.Info(InfoBarSeverity.Error, msg: Plugin.GetLocalized("SunshineNotFound") ?? "Sunshine configuration file not found. Please ensure Sunshine is installed.");
                return;
            }

            string jsonContent = await File.ReadAllTextAsync(sunshineConfigPath);

            // Using Newtonsoft.Json to match SunshineModels.cs
            var config = JsonConvert.DeserializeObject<SunshineConfig>(jsonContent) ?? new SunshineConfig();
            config.Apps ??= new List<SunshineApp>();

            string targetAppName = game.Name.Value ?? "Unknown Game";

            // New Command Format: cmd /c "start potato-vn://start/{uuid}"
            var cmdString = $"cmd /c \"start {paths.UuidUri}\"";
            // Image Filename for covers directory
            var targetImageName = $"app_{paths.Uuid}.png";

            var appEntry = config.Apps.FirstOrDefault(a => a.Name == targetAppName);

            if (appEntry != null)
            {
                appEntry.Cmd = cmdString;
                appEntry.ImagePath = targetImageName;
                appEntry.AutoDetach = "true";
                appEntry.WaitAll = "true";
            }
            else
            {
                appEntry = new SunshineApp
                {
                    Name = targetAppName,
                    Cmd = cmdString,
                    ImagePath = targetImageName,
                    AutoDetach = "true",
                    WaitAll = "true",
                    ExitTimeout = "5"
                };
                config.Apps.Add(appEntry);
            }

            var newJson = JsonConvert.SerializeObject(config, Formatting.Indented);

            // Always write elevated because we are updating Program Files and moving images
            await WriteFileElevatedAsync(newJson, sunshineConfigPath, paths.SunshineIconPath, targetImageName);
            
            // Reload Sunshine Configuration via API
            await ReloadSunshineConfigAsync(config.Apps);

            api.Info(InfoBarSeverity.Success, msg: Plugin.GetLocalized("SunshineExported") ?? "Exported to Sunshine successfully");
        }
        catch (Exception ex)
        {
            api.Info(InfoBarSeverity.Error, msg: Plugin.GetLocalized("SunshineExportFailed") ?? "Failed to export to Sunshine");
            Debug.WriteLine($"ExportToSunshine Error: {ex}");
        }
    }

    private static async Task ReloadSunshineConfigAsync(List<SunshineApp> apps)
    {
        int port = 47990;
        const string configPath = @"C:\Program Files\Sunshine\config\sunshine.conf";
        try 
        {
            if (File.Exists(configPath))
            {
                var lines = await File.ReadAllLinesAsync(configPath);
                foreach (var line in lines)
                {
                    var trim = line.Trim();
                    if (trim.StartsWith("port") && trim.Contains('='))
                    {
                        var val = trim.Split('=')[1].Trim();
                        if (int.TryParse(val, out int p)) port = p;
                    }
                }
            }
        }
        catch { }

        using var handler = new HttpClientHandler();
        handler.ClientCertificateOptions = ClientCertificateOption.Manual;
        handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

        using var client = new HttpClient(handler);
        var url = $"https://localhost:{port}/api/apps";
        
        var payload = new { apps = apps, editApp = (object?)null };
        var json = JsonConvert.SerializeObject(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(url, content);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Sunshine API Reload failed: {response.StatusCode}");
        }
    }

    private static async Task WriteFileElevatedAsync(string content, string destinationPath, string? sourceImagePath = null, string? targetImageFileName = null)
    {
        var tempJsonPath = Path.GetTempFileName();
        var tempScriptPath = Path.ChangeExtension(Path.GetTempFileName(), ".ps1");
        var resultPath = Path.GetTempFileName();
        
        try
        {
            // 1. Write JSON content to a temporary file
            await File.WriteAllTextAsync(tempJsonPath, content, Encoding.UTF8);

            // Ensure result file doesn't exist before running
            if (File.Exists(resultPath)) File.Delete(resultPath);

            // 2. Generate PowerShell script
            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine("try {");
            sb.AppendLine($"    $destJson = \"{destinationPath}\"");
            sb.AppendLine($"    $sourceJson = \"{tempJsonPath}\"");
            
            // Copy JSON
            sb.AppendLine("    Copy-Item -Path $sourceJson -Destination $destJson -Force");

            // Handle Image Migration
            if (!string.IsNullOrEmpty(sourceImagePath) && !string.IsNullOrEmpty(targetImageFileName))
            {
                sb.AppendLine($"    $sourceImg = \"{sourceImagePath}\"");
                sb.AppendLine($"    $targetImgName = \"{targetImageFileName}\"");
                sb.AppendLine("    $configDir = Split-Path $destJson -Parent");
                sb.AppendLine("    $coversDir = Join-Path $configDir \"covers\"");
                
                // Ensure covers directory exists
                sb.AppendLine("    if (-not (Test-Path $coversDir)) { New-Item -ItemType Directory -Path $coversDir -Force | Out-Null }");
                
                sb.AppendLine("    $destImg = Join-Path $coversDir $targetImgName");

                // Copy Image and Delete Original
                sb.AppendLine("    if (Test-Path $sourceImg) {");
                sb.AppendLine("        Copy-Item -Path $sourceImg -Destination $destImg -Force");
                sb.AppendLine("        Remove-Item -Path $sourceImg -Force");
                sb.AppendLine("    }");
            }
            
            // Signal Success
            sb.AppendLine($"    \"SUCCESS\" | Out-File -FilePath \"{resultPath}\" -Encoding UTF8");
            sb.AppendLine("} catch {");
            // Signal Failure
            sb.AppendLine($"    $_ | Out-File -FilePath \"{resultPath}\" -Encoding UTF8");
            sb.AppendLine("    exit 1");
            sb.AppendLine("}");

            await File.WriteAllTextAsync(tempScriptPath, sb.ToString(), Encoding.UTF8);

            // 3. Execute PowerShell script elevated
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };

            await Task.Run(() =>
            {
                var process = Process.Start(startInfo);
                process?.WaitForExit();
            });

            if (File.Exists(resultPath))
            {
                var result = await File.ReadAllTextAsync(resultPath);
                if (result.Trim() != "SUCCESS")
                {
                    throw new Exception($"Elevated script error: {result}");
                }
            }
            else
            {
                throw new Exception("Elevated process finished but returned no status.");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled elevation
            throw new Exception("User cancelled the elevation request.");
        }
        finally
        {
            if (File.Exists(tempJsonPath)) try { File.Delete(tempJsonPath); } catch { }
            if (File.Exists(tempScriptPath)) try { File.Delete(tempScriptPath); } catch { }
            if (File.Exists(resultPath)) try { File.Delete(resultPath); } catch { }
        }
    }
}
