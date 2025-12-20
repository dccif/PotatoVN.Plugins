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
using GalgameManager.Models;

namespace PotatoVN.App.PluginBase;

public partial class Plugin : IPlugin, IPluginSetting
{
    private IPotatoVnApi _hostApi = null!;
    private PluginData _data = new();
    private static ResourceManager? _resourceManager;
    private static CultureInfo? _pluginCulture;
    private Galgame? _activeGame;

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

        _hostApi.Messenger.Register<GalgamePlayedMessage>(this, (r, m) =>
        {
            if (m.Value != null)
            {
                _activeGame = m.Value;
                Debug.WriteLine($"Plugin 收到 GalgamePlayedMessage 消息: {m.Value.Name.Value}");

                // Only proceed if DetectedSavePath is null
                if (_activeGame.DetectedSavePath != null)
                {
                    Debug.WriteLine($"[Plugin] Save path already exists for {_activeGame.Name.Value}, skipping detection.");
                    return;
                }

                // Add background task if ExePath exists
                if (!string.IsNullOrEmpty(_activeGame.ExePath))
                {
                    // Use the new PluginSaveDetectorTask which encapsulates the logic
                    var task = new PluginSaveDetectorTask(m.Value, _hostApi.Messenger, _data.UseAdminMode);
                    _hostApi.AddBgTask(task);

                    Debug.WriteLine($"[Plugin] Added SaveDetection task for {_activeGame.Name.Value}");
                }
            }
        });
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