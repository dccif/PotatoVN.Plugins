using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using GalgameManager.Models;
using GalgameManager.WinApp.Base.Contracts.NavigationApi;
using GalgameManager.WinApp.Base.Contracts.NavigationApi.NavigateParameters;
using HarmonyLib;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PotatoVN.App.PluginBase.Models;
using PotatoVN.App.PluginBase.Services;

namespace PotatoVN.App.PluginBase.Views;

public partial class BigScreenPage : Grid
{
    private readonly Window _parentWindow;
    private readonly List<Galgame> _games;
    private readonly ContentControl _contentArea;
    private readonly Footer _footer;
    private readonly Header _header;
    private readonly BigScreenNavigationService _navService;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private Galgame? _lastSelectedGame;

    public BigScreenPage(Window parentWindow, List<Galgame> games, Galgame? initialGame = null)
    {
        _parentWindow = parentWindow;
        _games = games;
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _lastSelectedGame = initialGame;

        // Ensure Service is running
        GamepadService.Instance.Start();

        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 12, 15, 20));
        // Global XY Navigation
        XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

        // Header
        _header = new Header { IsTabStop = true };
        Children.Add(_header);
        Grid.SetRow(_header, 0);

        // Content Area
        _contentArea = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            IsTabStop = false,
            XYFocusUp = _header // Guide focus up to Header
        };
        Children.Add(_contentArea);
        Grid.SetRow(_contentArea, 1);

        // Footer
        _footer = new Footer();
        Children.Add(_footer);
        Grid.SetRow(_footer, 2);

        // Navigation Service Initialization
        _navService = new BigScreenNavigationService(_contentArea, ViewFactory);

        // Catch lost focus
        GotFocus += (s, e) =>
        {
            if (e.OriginalSource == this)
            {
                (_contentArea.Content as Control)?.Focus(FocusState.Programmatic);
            }
        };

        // Subscribe to Bus
        SimpleEventBus.Instance.Subscribe<NavigateToDetailMessage>(OnNavigateToDetail);
        SimpleEventBus.Instance.Subscribe<NavigateToLibraryMessage>(OnNavigateToLibrary);
        SimpleEventBus.Instance.Subscribe<AppExitMessage>(OnAppExit);
        SimpleEventBus.Instance.Subscribe<LaunchGameMessage>(OnLaunchGame);
        SimpleEventBus.Instance.Subscribe<UnhandledGamepadInputMessage>(OnUnhandledInput);

        // Initial View
        _navService.NavigateTo(ViewKey.Library, _lastSelectedGame);

        Unloaded += (s, e) =>
        {
            SimpleEventBus.Instance.Unsubscribe<NavigateToDetailMessage>(OnNavigateToDetail);
            SimpleEventBus.Instance.Unsubscribe<NavigateToLibraryMessage>(OnNavigateToLibrary);
            SimpleEventBus.Instance.Unsubscribe<AppExitMessage>(OnAppExit);
            SimpleEventBus.Instance.Unsubscribe<LaunchGameMessage>(OnLaunchGame);
            SimpleEventBus.Instance.Unsubscribe<UnhandledGamepadInputMessage>(OnUnhandledInput);

            // Do NOT stop GamepadService here, as the main window might still need it.
            // GamepadService.Instance.Stop(); 
        };
    }

    private BigScreenViewBase ViewFactory(ViewKey key, object? param)
    {
        switch (key)
        {
            case ViewKey.Library:
                return new GameLibraryView(_games, param as Galgame ?? _lastSelectedGame);
            case ViewKey.Detail:
                if (param is Galgame g) return new DetailView(g);
                throw new ArgumentException("Detail view requires a Galgame parameter");
            default:
                throw new ArgumentException($"Unknown ViewKey: {key}");
        }
    }

    private async void OnNavigateToDetail(NavigateToDetailMessage msg)
    {
        _lastSelectedGame = msg.Game;
        _dispatcherQueue.TryEnqueue(() =>
        {
            _navService.NavigateTo(ViewKey.Detail, msg.Game);
        });

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
                           Plugin.HostApi.NavigateTo(PageEnum.GalgamePage, new GalgamePageNavParameter { Galgame = msg.Game, StartGame = false })
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

    private void OnNavigateToLibrary(NavigateToLibraryMessage msg)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            _navService.NavigateTo(ViewKey.Library, _lastSelectedGame);
        });
    }

    private void OnAppExit(AppExitMessage msg)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            _parentWindow.Close();
        });
    }

    private void OnLaunchGame(LaunchGameMessage msg)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            System.Diagnostics.Debug.WriteLine($"Launching Game: {msg.Game.Name.Value}");
            // Here you would call the actual game launch service
        });
    }

    private void OnUnhandledInput(UnhandledGamepadInputMessage msg)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (msg.Button == GamepadButton.Up)
            {
                _header.FocusDefaultElement();
            }
        });
    }
}