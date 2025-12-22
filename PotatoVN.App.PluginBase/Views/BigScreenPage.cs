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
    
    private readonly List<Galgame> _games;
    private readonly Window _parentWindow;
    private HomePage? _homePage;

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

        // Initialize Home Page (Always alive)
        _homePage = new HomePage(_games);
        _mainLayer.Content = _homePage;

        // Register Messenger
        WeakReferenceMessenger.Default.Register<NavigateToDetailMessage>(this, (r, m) =>
        {
            OpenDetail(new DetailPage(m.Game));
            SyncToHost(m.Game);
        });

        WeakReferenceMessenger.Default.Register<NavigateToHomeMessage>(this, (r, m) =>
        {
            CloseDetail();
        });

        WeakReferenceMessenger.Default.Register<PlayGameMessage>(this, (r, m) =>
        {
             System.Diagnostics.Debug.WriteLine($"Launching {m.Game.Name.Value}");
        });

        Loaded += (s, e) => 
        {
            if (initialGame != null)
            {
                OpenDetail(new DetailPage(initialGame));
            }
            else
            {
                // Ensure focus on Home
                _homePage.Focus(FocusState.Programmatic);
            }
        };
        
        Unloaded += (s, e) => WeakReferenceMessenger.Default.UnregisterAll(this);
        
        // Handle Back navigation
        this.KeyDown += BigScreenPage_KeyDown;
    }

    private void OpenDetail(Page detailPage)
    {
        _overlayLayer.Content = detailPage;
        _overlayLayer.Visibility = Visibility.Visible;
        _mainLayer.Visibility = Visibility.Collapsed; // Hide main to prevent focus bleeding?
        // Actually, collapsing main layer WILL unload it and lose state. 
        // We must keep it Visible but IsHitTestVisible=False to preserve state.
        _mainLayer.Visibility = Visibility.Visible; 
        _mainLayer.IsHitTestVisible = false;
        
        // Move focus to new page
        detailPage.Focus(FocusState.Programmatic);
    }

    private void CloseDetail()
    {
        if (_overlayLayer.Visibility == Visibility.Visible)
        {
            _overlayLayer.Content = null;
            _overlayLayer.Visibility = Visibility.Collapsed;
            
            _mainLayer.IsHitTestVisible = true;
            
            // Restore focus to Home
            // Since Home was never unloaded, it should still have its state.
            // We just need to ensure focus returns to it.
            // If we just call Focus(), it might focus the Page itself.
            // We want to focus the *last focused element* inside Home.
            // Since we didn't unload, we didn't save state.
            // But 'FocusManager' usually tracks focus scope.
            // If focus was lost because we focused DetailPage, Home lost focus.
            
            // We can ask Home to recover focus.
            _homePage?.RestoreFocus();
        }
    }

    private void BigScreenPage_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.GamepadB || e.Key == Windows.System.VirtualKey.Escape)
        {
            if (_overlayLayer.Visibility == Visibility.Visible)
            {
                CloseDetail();
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
