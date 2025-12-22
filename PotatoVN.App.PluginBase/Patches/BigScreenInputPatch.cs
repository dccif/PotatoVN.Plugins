using HarmonyLib;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Windows.Foundation;
using PotatoVN.App.PluginBase.Views;
using PotatoVN.App.PluginBase.Models;
using System.Collections;

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
    private static CancellationTokenSource? _cts;
    private static Task? _pollingTask;

    // XInput P/Invoke
    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "#100")]
    private static extern int XInputGetStateEx(int dwUserIndex, out XINPUT_STATE pState);

    private const int ERROR_SUCCESS = 0;
    private const int XINPUT_GAMEPAD_GUIDE = 0x0400;

    public static void Attach(Page page)
    {
        if (_activePage == page) return;

        if (_activePage != null) Detach(_activePage);

        _activePage = page;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        // 1. Keyboard Hook
        page.KeyDown -= Page_KeyDown;
        page.KeyDown += Page_KeyDown;

        // 2. Gamepad Polling
        _cts = new CancellationTokenSource();
        _pollingTask = Task.Factory.StartNew(PollingLoop, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public static void Detach(Page page)
    {
        if (_activePage == page)
        {
            // Stop Keyboard
            page.KeyDown -= Page_KeyDown;

            // Stop Gamepad
            _cts?.Cancel();
            
            _activePage = null;
            _dispatcher = null;
        }
    }

    private static void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.F11)
        {
            if (_activePage != null)
            {
                 _dispatcher?.TryEnqueue(() => OpenBigScreen(_activePage));
            }
            e.Handled = true;
        }
    }

    private static async Task PollingLoop()
    {
        var token = _cts!.Token;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        bool wasPressed = false;

        while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
        {
            if (XInputGetStateEx(0, out var state) == ERROR_SUCCESS)
            {
                bool isPressed = (state.Gamepad.wButtons & XINPUT_GAMEPAD_GUIDE) != 0;
                
                if (isPressed && !wasPressed)
                {
                    _dispatcher?.TryEnqueue(() => 
                    {
                        if (_activePage != null) OpenBigScreen(_activePage);
                    });
                }
                wasPressed = isPressed;
            }
        }
    }

    private static async void OpenBigScreen(Page page)
    {
        // Prevent re-entry
        Detach(page);

        try
        {
            // 1. Show Loading UI
            var popup = new Microsoft.UI.Xaml.Controls.Primitives.Popup
            {
                XamlRoot = page.XamlRoot,
                IsHitTestVisible = true
            };

            var overlay = new Grid
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(220, 0, 0, 0)),
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

            // Resize handling
            TypedEventHandler<XamlRoot, XamlRootChangedEventArgs> sizeChangedHandler = (s, e) =>
            {
                overlay.Width = s.Size.Width;
                overlay.Height = s.Size.Height;
            };
            page.XamlRoot.Changed += sizeChangedHandler;

            // 2. Artificial Delay
            await Task.Delay(1200);

            // 3. Fetch Games
            List<GalgameManager.Models.Galgame> games = new();
            GalgameManager.Models.Galgame? initialGame = null;
            
            try
            {
                // Try get initial game
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

            // Re-attach when window closes
            window.Closed += (s, e) =>
            {
                 // Re-attach to the original page if still valid
                 // Note: 'page' might be dead if navigated away, but simple re-attach safe
                 Attach(page);
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening Big Screen: {ex}");
            Attach(page); // Re-attach on failure
        }
    }
}
