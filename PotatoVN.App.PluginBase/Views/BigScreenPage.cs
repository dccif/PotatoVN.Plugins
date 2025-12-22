using CommunityToolkit.Mvvm.Messaging;
using GalgameManager.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.PluginBase.Messages;
using System.Collections.Generic;
using System;
using System.Reflection;
using HarmonyLib;
using System.Threading.Tasks;
using GalgameManager.WinApp.Base.Contracts.NavigationApi.NavigateParameters;
using GalgameManager.WinApp.Base.Contracts.NavigationApi;

namespace PotatoVN.App.PluginBase.Views;

public sealed partial class BigScreenPage : Page
{
    private readonly Grid _rootGrid;
    private readonly ContentControl _mainLayer;
    private readonly ContentControl _overlayLayer;
    private readonly BigScreenNavigator _navigator;
    
    private readonly List<Galgame> _games;
    private readonly Window _parentWindow;

    public BigScreenPage(Window parentWindow, List<Galgame> games, Galgame? initialGame = null)
    {
        _parentWindow = parentWindow;
        _games = games;

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
        };
        
        Unloaded += (s, e) => WeakReferenceMessenger.Default.UnregisterAll(this);
        
        // Handle Back navigation
        this.KeyDown += BigScreenPage_KeyDown;
    }

    private void BigScreenPage_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.GamepadB || e.Key == Windows.System.VirtualKey.Escape)
        {
            if (_navigator.IsOverlayOpen)
            {
                _navigator.CloseOverlay();
                e.Handled = true;
            }
            else if (_navigator.CanGoBack)
            {
                _navigator.GoBack();
                e.Handled = true;
            }
            else
            {
                 _parentWindow.Close();
                 e.Handled = true;
            }
        }
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
