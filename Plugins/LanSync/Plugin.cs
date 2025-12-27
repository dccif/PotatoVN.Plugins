using GalgameManager.Enums;
using GalgameManager.WinApp.Base.Contracts;
using GalgameManager.WinApp.Base.Models;
using HarmonyLib;
using PotatoVN.App.PluginBase.Helper;
using PotatoVN.App.PluginBase.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase;

public partial class Plugin : IPlugin
{
    private static IPotatoVnApi _hostApi = null!;
    private PluginData _data = new();
    private Harmony? _harmony;
    private static ResourceManager? _resourceManager;
    private static CultureInfo? _pluginCulture;

    private static ResourceManager ResourceManager
    {
        get
        {
            if (_resourceManager == null)
                _resourceManager = new ResourceManager("PotatoVN.App.PluginBase.Properties.Resources",
                    typeof(Plugin).Assembly);
            return _resourceManager;
        }
    }

    internal static string? GetLocalized(string key)
    {
        return ResourceManager.GetString(key, _pluginCulture);
    }

    public PluginInfo Info { get; } = new()
    {
        Id = new Guid("b3d2315e-cb20-4abc-b8da-e238582b6522"),
        Name = "局域网存档同步",
        Description = "使得局域网中两个电脑上的存档同步"
    };

    public async Task InitializeAsync(IPotatoVnApi hostApi)
    {
        _hostApi = hostApi;
        FileHelper.Init(_hostApi.GetPluginPath());
        XamlResourceLocatorFactory.packagePath = _hostApi.GetPluginPath();
        var dataJson = await _hostApi.GetDataAsync();
        if (!string.IsNullOrWhiteSpace(dataJson))
            try
            {
                _data = System.Text.Json.JsonSerializer.Deserialize<PluginData>(dataJson) ?? new PluginData();
            }
            catch
            {
                _data = new PluginData();
            }

        _data.PropertyChanged += (_, _) => SaveData();

        // 1. Language Setup
        try
        {
            var language = _hostApi.Language;
            var cultureCode = language switch
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

                // Update Plugin Info with localized strings
                Info.Name = GetLocalized("PluginName") ?? "局域网存档同步";
                Info.Description = GetLocalized("PluginDescription") ??
                                   "使得局域网中两个电脑上的存档同步";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Plugin] Language Setup Error: {ex}");
        }
    }

    private void SaveData()
    {
        var dataJson = System.Text.Json.JsonSerializer.Serialize(_data);
        _ = _hostApi.SaveDataAsync(dataJson);
    }

    protected Guid Id => Info.Id;

    /// <summary>
    /// 插件卸载时清理资源
    /// </summary>
    public void Dispose()
    {
        try
        {
            _harmony?.UnpatchAll("com.potatovn.plugin.protocol");
        }
        catch
        {
            // 忽略卸载时的异常
        }
    }
}