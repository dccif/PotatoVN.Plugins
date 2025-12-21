using HarmonyLib;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using PotatoVN.App.PluginBase.Views;
using PotatoVN.App.PluginBase.Services;
using System.Collections;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using Windows.Foundation;
using PotatoVN.App.PluginBase.Models;

namespace PotatoVN.App.PluginBase.Patches;

public static class BigScreenInputPatch
{
    private static readonly HashSet<string> _targetPageNames = new()
    {
        "GalgameManager.Views.HomePage",
        "GalgameManager.Views.CategoryPage",
        "GalgameManager.Views.MultiStreamPage",
        "GalgameManager.Views.HomeDetailPage"
    };

    public static void Apply(Harmony harmony)
    {
        try
        {
            var pageType = typeof(Page);

            var onNavigatedTo = AccessTools.Method(pageType, "OnNavigatedTo");
            if (onNavigatedTo != null)
                harmony.Patch(onNavigatedTo, postfix: new HarmonyMethod(typeof(BigScreenInputPatch), nameof(OnNavigatedToPostfix)));

            var onNavigatedFrom = AccessTools.Method(pageType, "OnNavigatedFrom");
            if (onNavigatedFrom != null)
                harmony.Patch(onNavigatedFrom, postfix: new HarmonyMethod(typeof(BigScreenInputPatch), nameof(OnNavigatedFromPostfix)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BigScreenInputPatch Error: {ex}");
        }
    }

    public static void OnNavigatedToPostfix(object __instance)
    {
        if (__instance is Page page && _targetPageNames.Contains(page.GetType().FullName!))
        {
            BigScreenActionHandler.Attach(page);
        }
    }

    public static void OnNavigatedFromPostfix(object __instance)
    {
        if (__instance is Page page && _targetPageNames.Contains(page.GetType().FullName!))
        {
            BigScreenActionHandler.Detach(page);
        }
    }
}

public static class BigScreenActionHandler
{
    private static Page? _activePage;
    private static DispatcherQueue? _dispatcher;

    public static void Attach(Page page)
    {
        if (_activePage == page) return;

        if (_activePage != null) Detach(_activePage);

        _activePage = page;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        GamepadService.Instance.Start();
        SimpleEventBus.Instance.Subscribe<GamepadInputMessage>(OnGamepadInput);
    }

    public static void Detach(Page page)
    {
        if (_activePage == page)
        {
            SimpleEventBus.Instance.Unsubscribe<GamepadInputMessage>(OnGamepadInput);
            GamepadService.Instance.Stop();
            _activePage = null;
            _dispatcher = null;
        }
    }

    private static void OnGamepadInput(GamepadInputMessage msg)
    {
        if (msg.Button == GamepadButton.Guide && _activePage != null && _dispatcher != null)
        {
            _dispatcher.TryEnqueue(() => OpenBigScreen(_activePage));
        }
    }

    private static async void OpenBigScreen(Page page)
    {
        try
        {
            // Detach to prevent multiple triggers
            SimpleEventBus.Instance.Unsubscribe<GamepadInputMessage>(OnGamepadInput);

            // 1. Show Loading UI
            var popup = new Microsoft.UI.Xaml.Controls.Primitives.Popup
            {
                XamlRoot = page.XamlRoot,
                IsHitTestVisible = true // Block input
            };

            var overlay = new Grid
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(220, 0, 0, 0)),
                // Use XamlRoot size to ensure full coverage
                Width = page.XamlRoot.Size.Width,
                Height = page.XamlRoot.Size.Height,
            };

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 20
            };

            stack.Children.Add(new ProgressRing { IsActive = true, Width = 80, Height = 80 });

            // Localization
            var loadingText = Plugin.GetLocalized("EnteringBigScreen") ?? "Entering Big Screen Mode...";
            stack.Children.Add(new TextBlock
            {
                Text = loadingText,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiLight,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            overlay.Children.Add(stack);
            popup.Child = overlay;
            popup.IsOpen = true;

            // Adjust size if window resizes while loading
            TypedEventHandler<XamlRoot, XamlRootChangedEventArgs> sizeChangedHandler = (s, e) =>
            {
                overlay.Width = s.Size.Width;
                overlay.Height = s.Size.Height;
            };
            page.XamlRoot.Changed += sizeChangedHandler;

            // 2. Artificial Delay for smooth transition
            await Task.Delay(1200);

            // 3. Fetch Data (All Games) via Service Locator Reflection
            List<GalgameManager.Models.Galgame> games = new();
            GalgameManager.Models.Galgame? initialGame = null;
            try
            {
                // Attempt to get initial game if on Detail Page
                if (page.GetType().FullName == "GalgameManager.Views.HomeDetailPage")
                {
                    var vmProp = page.GetType().GetProperty("ViewModel");
                    if (vmProp != null)
                    {
                        var vm = vmProp.GetValue(page);
                        if (vm != null)
                        {
                            var itemProp = vm.GetType().GetProperty("Item");
                            if (itemProp != null)
                            {
                                initialGame = itemProp.GetValue(vm) as GalgameManager.Models.Galgame;
                            }
                        }
                    }
                }

                var appType = Application.Current.GetType();
                var getServiceMethod = appType.GetMethod("GetService");

                // Try multiple possible assembly locations for the interface
                var serviceType = Type.GetType("GalgameManager.Contracts.Services.IGalgameCollectionService, GalgameManager.WinApp.Base")
                                  ?? Type.GetType("GalgameManager.Contracts.Services.IGalgameCollectionService, GalgameManager.Core")
                                  ?? Type.GetType("GalgameManager.Contracts.Services.IGalgameCollectionService, GalgameManager");

                if (getServiceMethod != null && serviceType != null)
                {
                    var service = getServiceMethod.MakeGenericMethod(serviceType).Invoke(Application.Current, null);
                    if (service != null)
                    {
                        var galgamesProp = serviceType.GetProperty("Galgames");
                        if (galgamesProp != null)
                        {
                            var collection = galgamesProp.GetValue(service) as IEnumerable;
                            if (collection != null)
                            {
                                foreach (var item in collection)
                                {
                                    if (item is GalgameManager.Models.Galgame g)
                                        games.Add(g);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching games: {ex}");
            }

            // 4. Open Window
            popup.IsOpen = false;
            page.XamlRoot.Changed -= sizeChangedHandler;

            var window = new BigScreenWindow(games, initialGame);
            window.Activate();

            window.Closed += (s, e) =>
            {
                if (_activePage == page)
                {
                    SimpleEventBus.Instance.Subscribe<GamepadInputMessage>(OnGamepadInput);
                }
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening Big Screen: {ex}");
            // Restore handlers
            if (_activePage == page)
            {
                SimpleEventBus.Instance.Subscribe<GamepadInputMessage>(OnGamepadInput);
            }
        }
    }
}
