using GalgameManager.Enums;
using GalgameManager.Models;
using GalgameManager.WinApp.Base.Contracts;
using GalgameManager.WinApp.Base.Contracts.NavigationApi;
using GalgameManager.WinApp.Base.Contracts.NavigationApi.NavigateParameters;
using GalgameManager.WinApp.Base.Helpers;
using GalgameManager.WinApp.Base.Models;
using HarmonyLib;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using PotatoVN.App.PluginBase.Helper;
using PotatoVN.App.PluginBase.Models;
using PotatoVN.App.PluginBase.Services;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;

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
        FileHelper.Init(_hostApi.GetPluginPath());
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

        // 1. Language Setup
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

                // Update Plugin Info with localized strings
                Info.Name = GetLocalized("PluginName") ?? "Shortcut & Sunshine";
                Info.Description = GetLocalized("PluginDescription") ?? "Support creating desktop shortcuts (URI) and exporting to Sunshine.";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Plugin] Language Setup Error: {ex}");
        }

        // 2. URI Activation Handling
        try
        {          
            if (_hostApi.ActivationArgs is AppActivationArguments args &&
                args.Kind == ExtendedActivationKind.Protocol &&
                args.Data is ProtocolActivatedEventArgs protocolArgs)
            {
                var uri = protocolArgs.Uri; // potato-vn://start/{uuid}
                if (uri.Scheme == "potato-vn")
                {
                    bool startGame = false;
                    string uuidStr = "";

                    // potato-vn://start/uuid or potato-vn://view/uuid
                    if (uri.Host == "start")
                    {
                        startGame = true;
                        uuidStr = uri.AbsolutePath.TrimStart('/');
                    }
                    else if (uri.Host == "view")
                    {
                        startGame = false;
                        uuidStr = uri.AbsolutePath.TrimStart('/');
                    }

                    if (!string.IsNullOrEmpty(uuidStr) && Guid.TryParse(uuidStr, out Guid uuid))
                    {
                        var allGames = _hostApi.GetAllGames();
                        var game = allGames.FirstOrDefault(g => g.Uuid == uuid);
                        Debugger.Launch();
                        if (game != null)
                        {
                            try
                            {
                                var assembly = Assembly.Load("GalgameManager");
                                var helperType = assembly.GetType("GalgameManager.Helpers.UiThreadInvokeHelper");
                                var invokeMethod = AccessTools.Method(helperType, "InvokeAsync", new[] { typeof(Action) });

                                if (invokeMethod != null)
                                {
                                    await (Task)invokeMethod.Invoke(null, new object[] { new Action(() =>
                                        _hostApi.NavigateTo(PageEnum.GalgamePage, new GalgamePageNavParameter { Galgame = game, StartGame = startGame })
                                    ) });
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[Plugin] Navigation Error: {ex}");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Plugin] Activation Error: {ex}");
        }

        // 3. Hook Context Menu
        try
        {
            _harmony = new Harmony("com.potatovn.plugin.protocol");
            var assembly = Assembly.Load("GalgameManager");

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
