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
    private readonly ContentControl _viewContainer;
    private readonly List<Galgame> _games;
    private readonly Window _parentWindow;
    
    // Simple stack for "Back" navigation
    private readonly Stack<UIElement> _navStack = new();

    public BigScreenPage(Window parentWindow, List<Galgame> games, Galgame? initialGame = null)
    {
        _parentWindow = parentWindow;
        _games = games;

        // UI Construction: Use ContentControl instead of Frame
        _viewContainer = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        Content = _viewContainer;
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32));

        // Register Messenger
        WeakReferenceMessenger.Default.Register<NavigateToDetailMessage>(this, (r, m) =>
        {
            NavigateTo(new DetailPage(m.Game));
            SyncToHost(m.Game);
        });

        WeakReferenceMessenger.Default.Register<NavigateToHomeMessage>(this, (r, m) =>
        {
            GoBack();
        });

        WeakReferenceMessenger.Default.Register<PlayGameMessage>(this, (r, m) =>
        {
             System.Diagnostics.Debug.WriteLine($"Launching {m.Game.Name.Value}");
        });

        Loaded += (s, e) => 
        {
            try
            {
                if (initialGame != null)
                    NavigateTo(new DetailPage(initialGame), addToStack: false); // Initial View
                else
                    NavigateTo(new HomePage(_games), addToStack: false); // Initial View
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BigScreenPage] Init Failed: {ex}");
            }
        };
        
        Unloaded += (s, e) => WeakReferenceMessenger.Default.UnregisterAll(this);
        
        // Handle Back navigation
        this.KeyDown += BigScreenPage_KeyDown;
    }

    private void NavigateTo(UIElement newView, bool addToStack = true)
    {
        if (addToStack && _viewContainer.Content is UIElement currentView)
        {
            _navStack.Push(currentView);
        }
        _viewContainer.Content = newView;
    }

    private void GoBack()
    {
        if (_navStack.Count > 0)
        {
            var previousView = _navStack.Pop();
            _viewContainer.Content = previousView;
        }
        else
        {
            // If stack is empty but we are on DetailPage (initial load), swap to Home
            if (_viewContainer.Content is DetailPage)
            {
                 NavigateTo(new HomePage(_games), addToStack: false);
            }
        }
    }

    private void BigScreenPage_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.GamepadB || e.Key == Windows.System.VirtualKey.Escape)
        {
            if (_navStack.Count > 0)
            {
                GoBack();
                e.Handled = true;
            }
            else if (_viewContainer.Content is DetailPage)
            {
                // Edge case: Started on DetailPage, B should go to Home
                NavigateTo(new HomePage(_games), addToStack: false);
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