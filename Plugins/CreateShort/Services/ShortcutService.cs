using GalgameManager.Models;
using GalgameManager.WinApp.Base.Contracts;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;
using PotatoVN.App.PluginBase.Helper;
using PotatoVN.App.PluginBase.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase.Services;

public static class ShortcutService
{
    private record GamePaths(
        string ShortcutPath, // .url Path (Desktop)
        string VbsPath, // .vbs Path (Desktop)
        string LocalIconPath, // .ico Path (Host Images Folder)
        string SunshineIconPath, // .png Path (Pictures/sunshine)
        string UuidUri, // potato-vn://start/{uuid}
        string Uuid // {uuid}
    );

    private static async Task<GamePaths?> GetGamePathsAsync(Galgame game, string? tempSunshineDir = null)
    {
        var uuid = game.Uuid;
        var name = game.Name.Value;
        if (string.IsNullOrEmpty(name)) return null;

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var originalSafeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var safeBaseName = $"PotatoVN_{uuid}";

        var shortcutPath = Path.Combine(desktopPath, $"{originalSafeName}.url");
        var vbsPath = Path.Combine(desktopPath, $"{safeBaseName}.vbs");

        // Prepare .ico Icon Path
        var localImagesFolder = await FileHelper.GetImageFolderPathAsync();
        if (string.IsNullOrEmpty(localImagesFolder))
            localImagesFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PotatoVN", "ShortcutIcons");
        var localIconPath = Path.Combine(localImagesFolder, $"{originalSafeName}.ico");

        // Prepare .png Icon Path
        var sunshineIconPath = string.Empty;
        if (!string.IsNullOrEmpty(tempSunshineDir)) sunshineIconPath = Path.Combine(tempSunshineDir, $"{uuid}.png");

        return new GamePaths(shortcutPath, vbsPath, localIconPath, sunshineIconPath, $"potato-vn://start/{uuid}",
            uuid.ToString());
    }

    private static async Task EnsureAssetsAsync(Galgame game, GamePaths paths, bool needIco, bool needPng)
    {
        // 1. Create directories if needed
        if (needIco)
        {
            var localImagesFolder = Path.GetDirectoryName(paths.LocalIconPath);
            if (!string.IsNullOrEmpty(localImagesFolder) && !Directory.Exists(localImagesFolder))
                Directory.CreateDirectory(localImagesFolder);
        }

        if (needPng && !string.IsNullOrEmpty(paths.SunshineIconPath))
        {
            var sunshineDir = Path.GetDirectoryName(paths.SunshineIconPath);
            if (!string.IsNullOrEmpty(sunshineDir) && !Directory.Exists(sunshineDir))
                Directory.CreateDirectory(sunshineDir);
        }

        // 2. Resolve EXE path
        var exePath = game.ExePath;
        var localPath = game.LocalPath;
        var gameExePath = exePath;
        if (!string.IsNullOrEmpty(gameExePath) && !Path.IsPathRooted(gameExePath) && !string.IsNullOrEmpty(localPath))
            gameExePath = Path.Combine(localPath, gameExePath);

        if (string.IsNullOrEmpty(gameExePath) || !File.Exists(gameExePath)) return;

        // 3. Perform extractions
        var tasks = new List<Task>();

        // ICO is only needed for desktop shortcuts
        if (needIco && !File.Exists(paths.LocalIconPath))
            tasks.Add(IconHelper.ExtractBestIconAsync(gameExePath, paths.LocalIconPath));

        // PNG is only needed for Sunshine
        if (needPng && !string.IsNullOrEmpty(paths.SunshineIconPath) && !File.Exists(paths.SunshineIconPath))
            tasks.Add(IconHelper.SaveBestIconAsPngAsync(gameExePath, paths.SunshineIconPath));

        if (tasks.Count > 0) await Task.WhenAll(tasks);
    }

    public static async Task CreateDesktopShortcut(Galgame game, IPotatoVnApi api)
    {
        try
        {
            // 1. 快速获取路径 (无IO/提取)
            var paths = await GetGamePathsAsync(game, null);
            if (paths == null) return;

            // 2. 检查是否已存在且指向正确的 URI (快速检查，避免重复提取图标)
            if (File.Exists(paths.ShortcutPath))
                try
                {
                    var existingContent = await File.ReadAllTextAsync(paths.ShortcutPath);
                    if (existingContent.Contains($"URL={paths.UuidUri}"))
                    {
                        api.Info(InfoBarSeverity.Success,
                            msg: Plugin.GetLocalized("ShortcutAlreadyExists") ?? "Shortcut already exists on desktop.");
                        return;
                    }
                }
                catch
                {
                    /* 忽略读取错误，继续创建流程 */
                }

            // 3. 确保资源存在 (仅提取 ICO)
            await EnsureAssetsAsync(game, paths, true, false);

            // 4. 确定图标路径
            var iconPath = paths.LocalIconPath;
            if (!File.Exists(iconPath))
            {
                var appExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(appExePath)) iconPath = appExePath;
            }

            // 5. 构建内容
            var urlContent = new StringBuilder();
            urlContent.AppendLine("[InternetShortcut]");
            urlContent.AppendLine("IDList=");
            urlContent.AppendLine($"URL={paths.UuidUri}");

            if (!string.IsNullOrEmpty(iconPath))
            {
                urlContent.AppendLine("IconIndex=0");
                urlContent.AppendLine($"IconFile={iconPath}");
            }

            urlContent.AppendLine("");
            urlContent.AppendLine("[{000214A0-0000-0000-C000-000000000046}]");
            urlContent.AppendLine("Prop3=19,0");

            if (File.Exists(paths.ShortcutPath)) File.Delete(paths.ShortcutPath);

            await File.WriteAllTextAsync(paths.ShortcutPath, urlContent.ToString(), Encoding.Unicode);
            api.Info(InfoBarSeverity.Success,
                msg: Plugin.GetLocalized("ShortcutCreated") ?? "Desktop shortcut created successfully");
        }
        catch (Exception ex)
        {
            api.Info(InfoBarSeverity.Error,
                msg: Plugin.GetLocalized("ShortcutCreateFailed") ?? "Failed to create desktop shortcut");
            Debug.WriteLine($"CreateDesktopShortcut Error: {ex}");
        }
    }

    public static async Task ExportToSunshine(Galgame game, IPotatoVnApi api)
    {
        string? tempDir = null;
        try
        {
            // 1. Prepare Paths (Fast)
            tempDir = Path.Combine(Path.GetTempPath(), $"PotatoVN_Sunshine_{Guid.NewGuid()}");
            var paths = await GetGamePathsAsync(game, tempDir);
            if (paths == null) return;

            // 2. Try to fetch current apps list (API first, then File)
            List<SunshineApp>? currentApps = null;
            var isApiMode = false;
            const string sunshineConfigPath = @"C:\Program Files\Sunshine\config\apps.json";

            try
            {
                currentApps = await GetSunshineAppsAsync();
                isApiMode = true;
            }
            catch
            {
                if (File.Exists(sunshineConfigPath))
                    try
                    {
                        var json = await File.ReadAllTextAsync(sunshineConfigPath);
                        var config = JsonConvert.DeserializeObject<SunshineConfig>(json);
                        currentApps = config?.Apps ?? new List<SunshineApp>();
                    }
                    catch
                    {
                        /* Ignore file parse errors */
                    }
            }

            if (currentApps == null)
            {
                api.Info(InfoBarSeverity.Error,
                    msg: Plugin.GetLocalized("SunshineNotFound") ??
                         "Sunshine configuration file not found. Please ensure Sunshine is installed.");
                return;
            }

            // 3. Check for duplicates
            var uuidStr = game.Uuid.ToString();
            var alreadyExists = currentApps.Any(app => !string.IsNullOrEmpty(app.Cmd) && app.Cmd.Contains(uuidStr));

            if (alreadyExists)
            {
                api.Info(InfoBarSeverity.Success,
                    msg: Plugin.GetLocalized("SunshineAlreadyExists") ?? "Application already exists in Sunshine.");
                return;
            }

            // 4. Create Temp Directory & Generate Assets (仅提取 PNG)
            Directory.CreateDirectory(tempDir);
            await EnsureAssetsAsync(game, paths, false, true);

            // 5. Execute Export
            if (isApiMode)
                await ExportToSunshineRuntimeAsync(game, paths, currentApps, api);
            else
                await ExportToSunshineFileModeAsync(game, paths, api);
        }
        catch (Exception ex)
        {
            api.Info(InfoBarSeverity.Error,
                msg: Plugin.GetLocalized("SunshineExportFailed") ?? "Failed to export to Sunshine");
            Debug.WriteLine($"ExportToSunshine Error: {ex}");
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    /* Ignore cleanup errors */
                }
        }
    }

    private static async Task ExportToSunshineRuntimeAsync(Galgame game, GamePaths paths, List<SunshineApp> currentApps,
        IPotatoVnApi api)
    {
        try
        {
            // 1. Upload Image
            var imageKey = await UploadImageToSunshineAsync(paths.SunshineIconPath, paths.Uuid);

            // 2. Prepare App Data
            var targetAppName = game.Name.Value ?? "Unknown Game";
            var cmdString = $"cmd /c \"start {paths.UuidUri}\"";

            var targetApp = currentApps.FirstOrDefault(a => a.Name == targetAppName);
            if (targetApp != null)
            {
                targetApp.Index = currentApps.IndexOf(targetApp);
                targetApp.Cmd = cmdString;
                targetApp.ImagePath = imageKey;
                targetApp.AutoDetach = "true";
                targetApp.WaitAll = "true";
            }
            else
            {
                targetApp = new SunshineApp
                {
                    Name = targetAppName,
                    Cmd = cmdString,
                    ImagePath = imageKey,
                    AutoDetach = "true",
                    WaitAll = "true",
                    ExitTimeout = "5",
                    Index = -1
                };
            }

            await SaveSunshineAppsAsync(new List<SunshineApp>(), targetApp);
            api.Info(InfoBarSeverity.Success,
                msg: Plugin.GetLocalized("SunshineExported") ?? "Exported to Sunshine successfully");
        }
        catch (Exception ex)
        {
            throw new Exception($"Runtime export failed: {ex.Message}", ex);
        }
    }

    private static async Task ExportToSunshineFileModeAsync(Galgame game, GamePaths paths, IPotatoVnApi api)
    {
        const string sunshineConfigPath = @"C:\Program Files\Sunshine\config\apps.json";
        if (!File.Exists(sunshineConfigPath))
        {
            Debug.WriteLine("Sunshine config not found.");
            api.Info(InfoBarSeverity.Error,
                msg: Plugin.GetLocalized("SunshineNotFound") ??
                     "Sunshine configuration file not found. Please ensure Sunshine is installed.");
            return;
        }

        var jsonContent = await File.ReadAllTextAsync(sunshineConfigPath);
        var config = JsonConvert.DeserializeObject<SunshineConfig>(jsonContent) ?? new SunshineConfig();
        config.Apps ??= new List<SunshineApp>();

        var targetAppName = game.Name.Value ?? "Unknown Game";
        var cmdString = $"cmd /c \"start {paths.UuidUri}\"";
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

        await WriteFileElevatedAsync(newJson, sunshineConfigPath, paths.SunshineIconPath, targetImageName);

        try
        {
            await SaveSunshineAppsAsync(config.Apps);
        }
        catch
        {
            /* Ignore */
        }

        api.Info(InfoBarSeverity.Success,
            msg: Plugin.GetLocalized("SunshineExported") ?? "Exported to Sunshine successfully");
    }

    private static async Task<HttpClient> GetSunshineHttpClientAsync()
    {
        var port = 47990;
        var username = "admin";
        var password = "admin";
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
                        if (int.TryParse(val, out var p)) port = p;
                    }
                }
            }
        }
        catch
        {
        }

        var handler = new HttpClientHandler();
        handler.ClientCertificateOptions = ClientCertificateOption.Manual;
        handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

        var client = new HttpClient(handler);
        client.BaseAddress = new Uri($"https://127.0.0.1:{port}/");

        // Add Basic Auth header (matching PS1 script logic)
        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);

        return client;
    }

    private static async Task<List<SunshineApp>> GetSunshineAppsAsync()
    {
        using var client = await GetSunshineHttpClientAsync();
        var response = await client.GetAsync("api/apps");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var wrapper = JsonConvert.DeserializeObject<SunshineConfig>(json);
        return wrapper?.Apps ?? new List<SunshineApp>();
    }

    private static async Task<string> UploadImageToSunshineAsync(string localImagePath, string uuid)
    {
        if (!File.Exists(localImagePath)) return string.Empty;

        using var client = await GetSunshineHttpClientAsync();

        var bytes = await File.ReadAllBytesAsync(localImagePath);
        var base64 = Convert.ToBase64String(bytes);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var key = $"app_{uuid}_{timestamp}";

        var payload = new { key, data = base64 };
        var json = JsonConvert.SerializeObject(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/covers/upload", content);
        response.EnsureSuccessStatusCode();

        return $"{key}.png";
    }

    private static async Task SaveSunshineAppsAsync(List<SunshineApp> apps, SunshineApp? editApp = null)
    {
        using var client = await GetSunshineHttpClientAsync();

        var payload = new { apps, editApp };
        var json = JsonConvert.SerializeObject(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/apps", content);
        response.EnsureSuccessStatusCode();
    }

    private static async Task WriteFileElevatedAsync(string content, string destinationPath,
        string? sourceImagePath = null, string? targetImageFileName = null)
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
                sb.AppendLine(
                    "    if (-not (Test-Path $coversDir)) { New-Item -ItemType Directory -Path $coversDir -Force | Out-Null }");

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
                if (result.Trim() != "SUCCESS") throw new Exception($"Elevated script error: {result}");
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
            if (File.Exists(tempJsonPath))
                try
                {
                    File.Delete(tempJsonPath);
                }
                catch
                {
                }

            if (File.Exists(tempScriptPath))
                try
                {
                    File.Delete(tempScriptPath);
                }
                catch
                {
                }

            if (File.Exists(resultPath))
                try
                {
                    File.Delete(resultPath);
                }
                catch
                {
                }
        }
    }
}