using System;
using System.Reflection;
using System.Resources;
using System.Globalization;
using System.Threading.Tasks;
using GalgameManager.WinApp.Base.Contracts;
using GalgameManager.WinApp.Base.Models;
using GalgameManager.Models;
using HarmonyLib;
using PotatoVN.App.PluginBase.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using PotatoVN.App.PluginBase.Services;

namespace PotatoVN.App.PluginBase;

public partial class Plugin : IPlugin
{
    private IPotatoVnApi _hostApi = null!;
    private PluginData _data = new();
    private Harmony? _harmony;
    private static ResourceManager? _resourceManager;
    private static CultureInfo? _pluginCulture;

    private static ResourceManager ResourceManager
    {
        get
        {
            if (_resourceManager == null)
                _resourceManager = new ResourceManager("PotatoVN.App.PluginBase.Properties.Resources", typeof(Plugin).Assembly);
            return _resourceManager;
        }
    }

    private static string? GetLocalized(string key) => ResourceManager.GetString(key, _pluginCulture);

    public PluginInfo Info { get; } = new()
    {
        Id = new Guid("9e3286fe-2f75-4b7a-8951-f8d68b304eb0"),
        Name = "Shortcut & Sunshine",
        Description = "Support creating desktop shortcuts (URI) and exporting to Sunshine.",
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

        try
        {
            _harmony = new Harmony("com.potatovn.plugin.protocol");
            var assembly = Assembly.Load("GalgameManager");

            // --- Get language setting from main app ---
            var appType = assembly.GetType("GalgameManager.App");
            if (appType != null)
            {
                var getServiceMethod = appType.GetMethod("GetService", BindingFlags.Public | BindingFlags.Static);
                var localSettingsServiceType = assembly.GetType("GalgameManager.Contracts.Services.ILocalSettingsService");
                var languageEnumType = assembly.GetType("GalgameManager.Enums.LanguageEnum");
                var keyValuesType = assembly.GetType("GalgameManager.Enums.KeyValues");

                if (getServiceMethod != null && localSettingsServiceType != null && languageEnumType != null && keyValuesType != null)
                {
                    var genericGetServiceMethod = getServiceMethod.MakeGenericMethod(localSettingsServiceType);
                    var localSettingsServiceInstance = genericGetServiceMethod.Invoke(null, null);

                    var languageKeyField = keyValuesType.GetField("Language", BindingFlags.Public | BindingFlags.Static);
                    var languageKeyValue = languageKeyField?.GetValue(null);

                    if (localSettingsServiceInstance != null && languageKeyValue != null)
                    {
                        var readSettingMethod = localSettingsServiceInstance.GetType().GetMethod("ReadSettingAsync");
                        if (readSettingMethod != null)
                        {
                            var genericReadSettingMethod = readSettingMethod.MakeGenericMethod(languageEnumType);
                            // ReadSettingAsync has optional parameters: key, isLarge, converters, typeNameHandling
                            // We need to provide all of them for MethodInfo.Invoke
                            var taskResult = (Task?)genericReadSettingMethod.Invoke(localSettingsServiceInstance, new object?[] { languageKeyValue, false, null, false });

                            if (taskResult != null)
                            {
                                await taskResult;
                                var languageEnumValue = taskResult.GetType().GetProperty("Result")?.GetValue(taskResult);

                                if (languageEnumValue != null)
                                {
                                    var langStr = languageEnumValue.ToString();
                                    string cultureCode = langStr switch
                                    {
                                        "ChineseSimplified" => "zh-CN",
                                        "English" => "en-US",
                                        "Japanese" => "ja-JP",
                                        "Auto" => CultureInfo.InstalledUICulture.Name,
                                        _ => ""
                                    };

                                    if (!string.IsNullOrEmpty(cultureCode))
                                    {
                                        var culture = new CultureInfo(cultureCode);

                                        // 【关键修改】不仅设置线程文化，还保存下来传给 ResourceManager
                                        CultureInfo.CurrentUICulture = culture;
                                        CultureInfo.CurrentCulture = culture;
                                        _pluginCulture = culture;

                                        // Update Plugin Info with localized strings
                                        Info.Name = GetLocalized("PluginName") ?? "Shortcut & Sunshine";
                                        Info.Description = GetLocalized("PluginDescription") ?? "Support creating desktop shortcuts (URI) and exporting to Sunshine.";
                                    }
                                }
                            }
                        }
                    }
                }
            }
            // --- End get language setting ---

            // Hook HomeViewModel.GalFlyout_Opening
            var vmType = assembly.GetType("GalgameManager.ViewModels.HomeViewModel");
            if (vmType != null)
            {
                var original = AccessTools.Method(vmType, "GalFlyout_Opening");
                var postfix = AccessTools.Method(typeof(Plugin), nameof(GalFlyoutOpeningPostfix));
                if (original != null && postfix != null)
                {
                    _harmony.Patch(original, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Plugin Error: GalFlyout_Opening method not found on HomeViewModel.");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Plugin Hook Error: {ex}");
        }
    }




    private static void GalFlyoutOpeningPostfix(object sender)
    {
        try
        {
            if (sender is MenuFlyout flyout)
            {
                // Get the Galgame from flyout.Target.DataContext
                object? game = null;
                if (flyout.Target is FrameworkElement target && target.DataContext != null)
                {
                    game = target.DataContext;
                }

                InjectOrUpdateMenuItems(flyout, game);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GalFlyoutOpeningPostfix Error: {ex}");
        }
    }

    private static void InjectOrUpdateMenuItems(MenuFlyout flyout, object? game)
    {
        MenuFlyoutItem? shortcutItem = null;
        MenuFlyoutItem? sunshineItem = null;

        Galgame? castedGame = game as Galgame;

        // Check if already added
        foreach (var item in flyout.Items)
        {
            if (item.Tag as string == "Plugin_CreateShortcut" && item is MenuFlyoutItem mfi) shortcutItem = mfi;
            if (item.Tag as string == "Plugin_ExportSunshine" && item is MenuFlyoutItem mfi2) sunshineItem = mfi2;
        }

        // --- Create Shortcut Item ---
        if (shortcutItem == null)
        {
            shortcutItem = new MenuFlyoutItem
            {
                Text = GetLocalized("CreateShortcut") ?? "Create Desktop Shortcut (URI)",
                Icon = new SymbolIcon(Symbol.Link),
                Tag = "Plugin_CreateShortcut"
            };
            shortcutItem.Click += CreateShortcut_Click;
            AddItemToFlyout(flyout, shortcutItem);
        }
        shortcutItem.DataContext = game;
        shortcutItem.IsEnabled = game != null;

        // --- Export Sunshine Item ---
        if (sunshineItem == null)
        {
            sunshineItem = new MenuFlyoutItem
            {
                Text = GetLocalized("ExportToSunshine") ?? "Export to Sunshine",
                Icon = new SymbolIcon(Symbol.Share), // Or another symbol
                Tag = "Plugin_ExportSunshine"
            };
            sunshineItem.Click += ExportSunshine_Click;
            AddItemToFlyout(flyout, sunshineItem);
        }
        sunshineItem.DataContext = game;
        sunshineItem.IsEnabled = game != null;

        // Set visibility based on ExePath
        if (castedGame == null || string.IsNullOrEmpty(castedGame.ExePath))
        {
            shortcutItem.Visibility = Visibility.Collapsed;
            sunshineItem.Visibility = Visibility.Collapsed;
        }
        else
        {
            shortcutItem.Visibility = Visibility.Visible;
            sunshineItem.Visibility = Visibility.Visible;
        }
    }

    private static void AddItemToFlyout(MenuFlyout flyout, MenuFlyoutItem item)
    {
        // Find insertion point
        int insertIndex = -1;

        // Strategy 1: Check Icon (Symbol.OpenLocal)
        for (int i = 0; i < flyout.Items.Count; i++)
        {
            if (flyout.Items[i] is MenuFlyoutItem existingItem)
            {
                if (existingItem.Icon is SymbolIcon symIcon && symIcon.Symbol == Symbol.OpenLocal)
                {
                    insertIndex = i + 1;
                    break;
                }
            }
        }

        if (insertIndex != -1 && insertIndex <= flyout.Items.Count)
        {
            flyout.Items.Insert(insertIndex, item);
        }
        else
        {
            flyout.Items.Add(item);
        }
    }

    private static async void CreateShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement item && item.DataContext != null)
        {
            await ShortcutService.CreateDesktopShortcut(item.DataContext);
        }
    }

    private static async void ExportSunshine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement item && item.DataContext != null)
        {
            await ShortcutService.ExportToSunshine(item.DataContext);
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
