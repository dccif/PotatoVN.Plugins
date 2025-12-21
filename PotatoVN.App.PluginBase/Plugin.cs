using GalgameManager.Enums;
using GalgameManager.WinApp.Base.Contracts;
using GalgameManager.WinApp.Base.Contracts.PluginUi;
using GalgameManager.WinApp.Base.Models;
using PotatoVN.App.PluginBase.Models;
using PotatoVN.App.PluginBase.Patches;
using HarmonyLib;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase;

public partial class Plugin : IPlugin, IPluginSetting
{
    private IPotatoVnApi _hostApi = null!;
    internal static IPotatoVnApi? HostApi { get; private set; }
    private PluginData _data = new();
    private static ResourceManager? _resourceManager;
    private static CultureInfo? _pluginCulture;

    public PluginInfo Info { get; } = new()
    {
        Id = new Guid("ae7cbd73-4664-4733-80c5-83d0b119a174"),
        Name = "大屏模式",
        Description = "让你可以进入大屏模式",
    };

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

    public async Task InitializeAsync(IPotatoVnApi hostApi)
    {
        _hostApi = hostApi;
        HostApi = hostApi;
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
        _data.PropertyChanged += (_, _) => SaveData(); // 当Observable属性变化时自动保存数据，对于普通属性请手动调用SaveData

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

        try
        {
            var harmony = new Harmony("PotatoVN.App.Plugin.BigScreen");
            BigScreenInputPatch.Apply(harmony);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Plugin] Harmony Patch Error: {ex}");
        }
    }

    private void SaveData()
    {
        var dataJson = System.Text.Json.JsonSerializer.Serialize(_data);
        _ = _hostApi.SaveDataAsync(dataJson);
    }

    protected Guid Id => Info.Id;

    // GamepadService is a singleton and lives for the app lifetime.
    // Explicit disposal is not required here unless plugin unload is supported.
}

