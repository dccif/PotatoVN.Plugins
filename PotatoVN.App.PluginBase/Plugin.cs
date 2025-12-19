using GalgameManager.WinApp.Base.Contracts;
using GalgameManager.WinApp.Base.Models;
using GalgameManager.WinApp.Base.Models.Msgs;
using CommunityToolkit.Mvvm.Messaging;
using PotatoVN.App.PluginBase.Models;
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using GalgameManager.WinApp.Base.Contracts.PluginUi;
using System.Globalization;
using System.Resources;
using GalgameManager.Enums;
using PotatoVN.App.PluginBase.Services;
using System.Threading;
using GalgameManager.Helpers;
using GalgameManager.Models;
using Microsoft.UI.Xaml.Controls;

namespace PotatoVN.App.PluginBase
{
    public partial class Plugin : IPlugin, IPluginSetting
    {
        private IPotatoVnApi _hostApi = null!;
        private PluginData _data = new();
        private static ResourceManager? _resourceManager;
        private static CultureInfo? _pluginCulture;
        private Galgame? _activeGame;
        private CancellationTokenSource? _monitorCts;

        private static ResourceManager ResourceManager
        {
            get
            {
                if (_resourceManager == null)
                    _resourceManager = new ResourceManager("PotatoVN.App.PluginBase.Properties.Resources", typeof(Plugin).Assembly);
                return _resourceManager;
            }
        }

        internal static string? GetLocalized(string key) => ResourceManager.GetString(key, _pluginCulture);

        public PluginInfo Info { get; } = new()
        {
            Id = new Guid("a8b3c9d2-4e7f-5a6b-8c9d-1e2f3a4b5c6d"),
            Name = "存档位置探测",
            Description = "游戏存档位置探测器，使用算法实时监控文件变更，定位游戏存档位置。",
        };

        public async Task InitializeAsync(IPotatoVnApi hostApi)
        {
            _hostApi = hostApi;
            XamlResourceLocatorFactory.packagePath = _hostApi.GetPluginPath();
            var dataJson = await _hostApi.GetDataAsync();
            if (!string.IsNullOrWhiteSpace(dataJson))
            {
                try
                {
                    _data = System.Text.Json.JsonSerializer.Deserialize<PluginData>(dataJson) ?? new PluginData();
                }
                catch
                {
                    _data = new PluginData();
                }
            }
            _data.PropertyChanged += (_, _) => SaveData();

            // Language Setup
            try
            {
                var language = _hostApi.Language;
                string cultureCode = language switch
                {
                    LanguageEnum.ChineseSimplified => "zh-CN",
                    LanguageEnum.English => "en-US",
                    LanguageEnum.Japanese => "ja-JP",
                    _ => CultureInfo.InstalledUICulture.Name
                };

                if (!string.IsNullOrEmpty(cultureCode))
                {
                    var culture = new CultureInfo(cultureCode);
                    CultureInfo.CurrentUICulture = culture;
                    CultureInfo.CurrentCulture = culture;
                    _pluginCulture = culture;

                    var name = GetLocalized("PluginName");
                    if (!string.IsNullOrEmpty(name)) Info.Name = name;

                    var desc = GetLocalized("PluginDescription");
                    if (!string.IsNullOrEmpty(desc)) Info.Description = desc;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Plugin] Language Setup Error: {ex}");
            }

            _hostApi.Messenger.Register<GalgamePlayedMessage>(this, async (r, m) =>
            {
                if (m.Value != null)
                {
                    _activeGame = m.Value;
                    Debug.WriteLine($"Plugin 收到 GalgamePlayedMessage 消息: {m.Value.Name}");

                    // Only proceed if DetectedSavePath is null
                    if (_activeGame.DetectedSavePath != null)
                    {
                        Debug.WriteLine($"[Plugin] Save path already exists for {_activeGame.Name}, skipping detection.");
                        return;
                    }
                    
                    // Attempt to auto-detect process from ExePath
                    if (!string.IsNullOrEmpty(_activeGame.ExePath))
                    {
                        await Task.Delay(2000); // Wait a bit for the game process to fully start
                        try
                        {
                            var exeName = System.IO.Path.GetFileNameWithoutExtension(_activeGame.ExePath);
                            var processes = Process.GetProcessesByName(exeName);
                            if (processes.Length > 0)
                            {
                                // Pick the first one for now, or maybe the one with a window?
                                var process = processes[0]; 
                                Debug.WriteLine($"[Plugin] Auto-detected process: {process.ProcessName} ({process.Id})");
                                
                                string title = GetLocalized("Msg_ProcessSelectedTitle") ?? "Process Selected";
                                string msgFormat = GetLocalized("Msg_ProcessSelectedMsg") ?? "Monitoring {0} for file changes...";
                                _hostApi.Info(InfoBarSeverity.Informational, title, string.Format(msgFormat, process.ProcessName));

                                _ = StartDetection(process);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Plugin] Auto-detection failed: {ex}");
                        }
                    }
                }
            });
        }

        public async Task StartDetection(Process process)
        {
            if (_activeGame == null)
            {
                Debug.WriteLine("[Plugin] No active game to set save path for.");
                return;
            }

            if (_activeGame.DetectedSavePath != null)
            {
                Debug.WriteLine("[Plugin] Save path already exists, ignoring detection.");
                return;
            }

            _monitorCts?.Cancel();
            _monitorCts = new CancellationTokenSource();
            var token = _monitorCts.Token;

            Debug.WriteLine($"[Plugin] Starting monitor for process {process.Id} ({process.ProcessName})");

            try
            {
                // Run in background
                string? detectedPath;
                if (_data.UseAdminMode)
                {
                    Debug.WriteLine("[Plugin] Using Admin Mode (ETW) for detection.");
                    detectedPath = await Task.Run(() => EtwSaveDetector.DetectSavePathAsync(process, _activeGame, token), token);
                }
                else
                {
                    Debug.WriteLine("[Plugin] Using Normal Mode (Polling) for detection.");
                    detectedPath = await Task.Run(() => SaveFileDetector.DetectSavePathAsync(process, _activeGame, token), token);
                }

                if (!string.IsNullOrEmpty(detectedPath))
                {
                    Debug.WriteLine($"[Plugin] Detected Save Path: {detectedPath}");

                    if (_activeGame != null)
                    {
                        // Using GamePortablePath.Create to properly format the path
                        _activeGame.DetectedSavePath = GamePortablePath.Create(detectedPath, _activeGame.LocalPath);

                        // Notify Success
                        string title = GetLocalized("Msg_SaveDetectedTitle") ?? "Save Path Detected";
                        string msgFormat = GetLocalized("Msg_SaveDetectedMsg") ?? "Path updated to: {0}";
                        _hostApi.Info(InfoBarSeverity.Success, title, string.Format(msgFormat, detectedPath));
                    }

                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Plugin] Detection Error: {ex}");
            }
        }

        private void SaveData()
        {
            var dataJson = System.Text.Json.JsonSerializer.Serialize(_data);
            _ = _hostApi.SaveDataAsync(dataJson);
        }

        public static bool IsAdministrator()
        {
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        public void RestartAsAdmin()
        {
            try
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = Environment.ProcessPath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(startInfo);
                Microsoft.UI.Xaml.Application.Current.Exit();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Plugin] Restart failed: {ex.Message}");
            }
        }

        protected Guid Id => Info.Id;
    }
}
