using CommunityToolkit.Mvvm.Messaging;
using GalgameManager.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PotatoVN.App.PluginBase.Messages;
using System.Collections.Generic;
using System;
using System.Reflection;
using HarmonyLib;
using System.Threading.Tasks;
using GalgameManager.WinApp.Base.Contracts.NavigationApi.NavigateParameters;
using GalgameManager.WinApp.Base.Contracts.NavigationApi;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Threading;
using Windows.Gaming.Input;

namespace PotatoVN.App.PluginBase.Views;

public sealed partial class BigScreenPage : Page
{
    private readonly Grid _rootGrid;
    private readonly ContentControl _mainLayer;
    private readonly ContentControl _overlayLayer;
    private readonly BigScreenNavigator _navigator;
    
    private readonly List<Galgame> _games;
    private readonly Window _parentWindow;
    private readonly DispatcherQueue _dispatcher;
    private CancellationTokenSource? _gamepadCts;
    private GamepadButtons _previousButtons;
    private bool _previousUp;
    private bool _previousDown;
    private bool _previousLeft;
    private bool _previousRight;
    private DateTime _lastSystemGamepadInputUtc;

    private const double ThumbThreshold = 0.5;
    private const int SystemGamepadSuppressMs = 250;

    public BigScreenPage(Window parentWindow, List<Galgame> games, Galgame? initialGame = null)
    {
        _parentWindow = parentWindow;
        _games = games;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        // UI Construction: Two Layers
        _rootGrid = new Grid();
        _mainLayer = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        _overlayLayer = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Visibility = Visibility.Collapsed };
        
        _rootGrid.Children.Add(_mainLayer);
        _rootGrid.Children.Add(_overlayLayer);
        
        Content = _rootGrid;
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32));

        _navigator = new BigScreenNavigator(_mainLayer, _overlayLayer);
        _navigator.Register(BigScreenRoute.Home, _ => new HomePage(_games), cache: true);
        _navigator.Register(BigScreenRoute.Detail, parameter =>
        {
            if (parameter is Galgame game)
            {
                return new DetailPage(game);
            }

            throw new ArgumentException("Detail page requires a Galgame parameter.", nameof(parameter));
        }, cache: false);

        // Register Messenger
        WeakReferenceMessenger.Default.Register<BigScreenNavigateMessage>(this, (r, m) =>
        {
            _navigator.Navigate(m.Route, m.Parameter, m.Mode);

            if (m.Route == BigScreenRoute.Detail && m.Parameter is Galgame game)
            {
                SyncToHost(game);
            }
        });

        WeakReferenceMessenger.Default.Register<BigScreenCloseOverlayMessage>(this, (r, m) =>
        {
            _navigator.CloseOverlay();
        });

        WeakReferenceMessenger.Default.Register<PlayGameMessage>(this, (r, m) =>
        {
             System.Diagnostics.Debug.WriteLine($"Launching {m.Game.Name.Value}");
        });

        Loaded += (s, e) => 
        {
            _navigator.Navigate(BigScreenRoute.Home);

            if (initialGame != null)
            {
                _navigator.Navigate(BigScreenRoute.Detail, initialGame, BigScreenNavMode.Overlay);
            }

            StartGamepadPolling();
        };
        
        Unloaded += (s, e) =>
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
            StopGamepadPolling();
        };
        
        // Handle input even if controls mark it handled, to keep footer hints in sync.
        AddHandler(KeyDownEvent, new KeyEventHandler(BigScreenPage_KeyDown), true);
    }

    public void RequestFocus()
    {
        _navigator.RequestFocusActivePage();
    }

    private void BigScreenPage_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var isGamepad = e.Key >= Windows.System.VirtualKey.GamepadA
            && e.Key <= Windows.System.VirtualKey.GamepadRightThumbstickLeft;
        if (isGamepad)
        {
            _lastSystemGamepadInputUtc = DateTime.UtcNow;
        }
        InputManager.ReportInput(isGamepad ? InputDeviceType.Gamepad : InputDeviceType.Keyboard);

        if (e.Key == Windows.System.VirtualKey.GamepadA)
        {
            InvokeFocusedElement();
            e.Handled = true;
            return;
        }

        if (!e.Handled && e.Key == Windows.System.VirtualKey.Enter)
        {
            InvokeFocusedElement();
            e.Handled = true;
            return;
        }

        if (e.Key == Windows.System.VirtualKey.GamepadB || e.Key == Windows.System.VirtualKey.Escape)
        {
            HandleBackNavigation();
            e.Handled = true;
        }
    }

    private void StartGamepadPolling()
    {
        StopGamepadPolling();
        _gamepadCts = new CancellationTokenSource();
        _ = Task.Run(() => PollGamepad(_gamepadCts.Token));
    }

    private void StopGamepadPolling()
    {
        _gamepadCts?.Cancel();
        _gamepadCts = null;
    }

    private async Task PollGamepad(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));

        try
        {
            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                var gamepad = Gamepad.Gamepads.Count > 0 ? Gamepad.Gamepads[0] : null;
                if (gamepad == null) continue;

                var reading = gamepad.GetCurrentReading();
                var buttons = reading.Buttons;

                bool suppressNavigation = (DateTime.UtcNow - _lastSystemGamepadInputUtc).TotalMilliseconds < SystemGamepadSuppressMs;

                bool up = ((buttons & GamepadButtons.DPadUp) != 0) || reading.LeftThumbstickY > ThumbThreshold;
                bool down = ((buttons & GamepadButtons.DPadDown) != 0) || reading.LeftThumbstickY < -ThumbThreshold;
                bool left = ((buttons & GamepadButtons.DPadLeft) != 0) || reading.LeftThumbstickX < -ThumbThreshold;
                bool right = ((buttons & GamepadButtons.DPadRight) != 0) || reading.LeftThumbstickX > ThumbThreshold;

                bool anyInput = false;

                if (!suppressNavigation)
                {
                    if (up && !_previousUp)
                    {
                        anyInput = true;
                        EnqueueMoveFocus(FocusNavigationDirection.Up);
                    }
                    if (down && !_previousDown)
                    {
                        anyInput = true;
                        EnqueueMoveFocus(FocusNavigationDirection.Down);
                    }
                    if (left && !_previousLeft)
                    {
                        anyInput = true;
                        EnqueueMoveFocus(FocusNavigationDirection.Left);
                    }
                    if (right && !_previousRight)
                    {
                        anyInput = true;
                        EnqueueMoveFocus(FocusNavigationDirection.Right);
                    }
                }

                bool pressA = (buttons & GamepadButtons.A) != 0 && (_previousButtons & GamepadButtons.A) == 0;
                if (pressA)
                {
                    anyInput = true;
                    EnqueueInvokeFocused();
                }

                bool pressB = (buttons & GamepadButtons.B) != 0 && (_previousButtons & GamepadButtons.B) == 0;
                if (pressB)
                {
                    anyInput = true;
                    EnqueueBack();
                }

                if (anyInput)
                {
                    _dispatcher.TryEnqueue(() => InputManager.ReportInput(InputDeviceType.Gamepad));
                }

                _previousButtons = buttons;
                _previousUp = up;
                _previousDown = down;
                _previousLeft = left;
                _previousRight = right;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void EnqueueMoveFocus(FocusNavigationDirection direction)
    {
        _dispatcher.TryEnqueue(() =>
        {
            var root = GetFocusSearchRoot();
            if (root == null) return;

            try
            {
                var options = new FindNextElementOptions { SearchRoot = root };
                if (!FocusManager.TryMoveFocus(direction, options))
                {
                    _navigator.RequestFocusActivePage();
                    FocusManager.TryMoveFocus(direction, options);
                }
            }
            catch
            {
                _navigator.RequestFocusActivePage();
            }
        });
    }

    private void EnqueueInvokeFocused()
    {
        _dispatcher.TryEnqueue(InvokeFocusedElement);
    }

    private void EnqueueBack()
    {
        _dispatcher.TryEnqueue(HandleBackNavigation);
    }

    private void HandleBackNavigation()
    {
        if (_navigator.IsOverlayOpen)
        {
            _navigator.CloseOverlay();
        }
        else if (_navigator.CanGoBack)
        {
            _navigator.GoBack();
        }
        else
        {
            _parentWindow.Close();
        }
    }

    private void InvokeFocusedElement()
    {
        var focused = GetFocusedElement();
        if (focused == null) return;

        var button = FindAncestor<ButtonBase>(focused);
        if (button != null)
        {
            InvokeViaAutomation(button);
            return;
        }

        var game = ResolveFocusedGame(focused);
        if (game != null)
        {
            var page = FindAncestor<Page>(focused) ?? GetActivePage();
            if (page is HomePage home && home.ViewModel != null)
            {
                home.ViewModel.ItemClickCommand.Execute(game);
                return;
            }
        }

        var gridItem = FindAncestor<GridViewItem>(focused);
        if (gridItem != null)
        {
            InvokeViaAutomation(gridItem);
        }
    }

    private static void InvokeViaAutomation(FrameworkElement element)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(element)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
        if (peer == null) return;

        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invokeProvider)
        {
            invokeProvider.Invoke();
            return;
        }

        if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selectionProvider)
        {
            selectionProvider.Select();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T match) return match;
            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private static Galgame? ResolveFocusedGame(DependencyObject focused)
    {
        if (focused is FrameworkElement element && element.DataContext is Galgame directGame)
        {
            return directGame;
        }

        var gridItem = FindAncestor<GridViewItem>(focused);
        if (gridItem == null)
        {
            var gridView = FindAncestor<GridView>(focused);
            if (gridView != null)
            {
                if (gridView.SelectedItem is Galgame selectedGame)
                {
                    return selectedGame;
                }

                if (gridView.ItemsPanelRoot is Panel panel)
                {
                    foreach (var child in panel.Children)
                    {
                        if (child is Control control && control.FocusState != FocusState.Unfocused)
                        {
                            if (control.DataContext is Galgame focusedGame)
                            {
                                return focusedGame;
                            }

                            if (control is FrameworkElement focusedElement && focusedElement.DataContext is Galgame elementGame)
                            {
                                return elementGame;
                            }
                        }
                    }
                }
            }

            return null;
        }

        if (gridItem.DataContext is Galgame game)
        {
            return game;
        }

        if (gridItem.Content is FrameworkElement contentElement && contentElement.DataContext is Galgame contentGame)
        {
            return contentGame;
        }

        if (gridItem.ContentTemplateRoot is FrameworkElement templateRoot && templateRoot.DataContext is Galgame rootGame)
        {
            return rootGame;
        }

        return null;
    }

    private DependencyObject? GetFocusedElement()
    {
        var focused = FocusManager.GetFocusedElement() as DependencyObject;
        if (focused != null)
        {
            return focused;
        }

        _navigator.RequestFocusActivePage();
        focused = FocusManager.GetFocusedElement() as DependencyObject;
        if (focused != null)
        {
            return focused;
        }

        var root = GetFocusSearchRoot();
        if (root == null) return null;

        return FindFocusedDescendant(root);
    }

    private static DependencyObject? FindFocusedDescendant(DependencyObject root)
    {
        if (root is Control control && control.FocusState != FocusState.Unfocused)
        {
            return control;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var focused = FindFocusedDescendant(child);
            if (focused != null)
            {
                return focused;
            }
        }

        return null;
    }

    private Page? GetActivePage()
    {
        if (_navigator.IsOverlayOpen)
        {
            return _overlayLayer.Content as Page;
        }

        return _mainLayer.Content as Page;
    }

    private DependencyObject? GetFocusSearchRoot()
    {
        DependencyObject? root = null;

        if (_navigator.IsOverlayOpen)
        {
            root = _overlayLayer.Content as DependencyObject;
        }

        root ??= _mainLayer.Content as DependencyObject;
        root ??= _rootGrid;

        if (root is FrameworkElement element && !element.IsLoaded)
        {
            return null;
        }

        return root;
    }
    
    private async void SyncToHost(Galgame game)
    {
        if (Plugin.HostApi != null)
        {
             try
            {
                var assembly = Assembly.Load("GalgameManager");
                var helperType = assembly.GetType("GalgameManager.Helpers.UiThreadInvokeHelper");
                var invokeMethod = AccessTools.Method(helperType, "InvokeAsync", [typeof(Action)]);

                if (invokeMethod != null)
                {
#pragma warning disable CS8600
#pragma warning disable CS8602
                    await (Task)invokeMethod.Invoke(null, [ new Action(() =>
                           Plugin.HostApi.NavigateTo(PageEnum.GalgamePage, new GalgamePageNavParameter { Galgame = game, StartGame = false })
                    ) ]);
#pragma warning restore CS8602
#pragma warning restore CS8600
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BigScreen] Nav Sync Error: {ex}");
            }
        }
    }
}
