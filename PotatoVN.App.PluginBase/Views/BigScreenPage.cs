using GalgameManager.Enums;
using GalgameManager.Models;
using GalgameManager.WinApp.Base.Contracts.NavigationApi;
using GalgameManager.WinApp.Base.Contracts.NavigationApi.NavigateParameters;
using HarmonyLib;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PotatoVN.App.PluginBase.Models;
using PotatoVN.App.PluginBase.Services;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase.Views;

public class BigScreenPage : Grid
{
    private readonly Window _parentWindow;
    private readonly List<Galgame> _games;
    private readonly ContentControl _contentArea;
    private readonly Footer _footer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private Galgame? _lastSelectedGame;
    private GameLibraryView? _libraryView;

    public BigScreenPage(Window parentWindow, List<Galgame> games)
    {
        _parentWindow = parentWindow;
        _games = games;
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // Ensure Service is running
        GamepadService.Instance.Start();

        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 12, 15, 20));
        // We let children handle navigation, but keep this for global focus trapping if needed
        XYFocusKeyboardNavigation = Microsoft.UI.Xaml.Input.XYFocusKeyboardNavigationMode.Enabled;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

        // Header
        var header = new Header();
        Children.Add(header);
        Grid.SetRow(header, 0);

        // Content Area
        _contentArea = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            IsTabStop = false
        };
        Children.Add(_contentArea);
        Grid.SetRow(_contentArea, 1);

        // Footer
        _footer = new Footer();
        Children.Add(_footer);
        Grid.SetRow(_footer, 2);

        // Subscribe to Bus
        SimpleEventBus.Instance.Subscribe<NavigateToDetailMessage>(OnNavigateToDetail);
        SimpleEventBus.Instance.Subscribe<NavigateToLibraryMessage>(OnNavigateToLibrary);
        SimpleEventBus.Instance.Subscribe<AppExitMessage>(OnAppExit);
        SimpleEventBus.Instance.Subscribe<LaunchGameMessage>(OnLaunchGame);

        // Initial View
        ShowLibrary();

        Unloaded += (s, e) =>
        {
            SimpleEventBus.Instance.Unsubscribe<NavigateToDetailMessage>(OnNavigateToDetail);
            SimpleEventBus.Instance.Unsubscribe<NavigateToLibraryMessage>(OnNavigateToLibrary);
            SimpleEventBus.Instance.Unsubscribe<AppExitMessage>(OnAppExit);
            SimpleEventBus.Instance.Unsubscribe<LaunchGameMessage>(OnLaunchGame);

            // Do NOT stop GamepadService here, as the main window might still need it.
            // GamepadService.Instance.Stop(); 
        };
    }

    private async void OnNavigateToDetail(NavigateToDetailMessage msg)
    {
        _lastSelectedGame = msg.Game;
        _dispatcherQueue.TryEnqueue(() =>
        {
            _contentArea.Content = new DetailView(msg.Game);
        });

        if (Plugin.HostApi != null)
        {
            try
            {
                var assembly = Assembly.Load("GalgameManager");
                var helperType = assembly.GetType("GalgameManager.Helpers.UiThreadInvokeHelper");
                var invokeMethod = AccessTools.Method(helperType, "InvokeAsync", new[] { typeof(Action) });

                if (invokeMethod != null)
                {
#pragma warning disable CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
#pragma warning disable CS8602 // 解引用可能出现空引用。
                    await (Task)invokeMethod.Invoke(null, [ new Action(() =>
                           Plugin.HostApi.NavigateTo(PageEnum.GalgamePage, new GalgamePageNavParameter { Galgame = msg.Game, StartGame = false })
                    ) ]);
#pragma warning restore CS8602 // 解引用可能出现空引用。
#pragma warning restore CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
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
            ShowLibrary();
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

    private void ShowLibrary()
    {
        if (_libraryView == null)
        {
            _libraryView = new GameLibraryView(_games);
        }
        _contentArea.Content = _libraryView;
    }
}