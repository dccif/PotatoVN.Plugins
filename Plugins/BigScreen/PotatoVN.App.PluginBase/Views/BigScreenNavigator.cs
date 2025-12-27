using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.PluginBase.Messages;
using System;
using System.Collections.Generic;

namespace PotatoVN.App.PluginBase.Views;

public interface IBigScreenPage
{
    void OnNavigatedTo(object? parameter);
    void OnNavigatedFrom();
    void RequestFocus();
}

public sealed class BigScreenNavigator
{
    private sealed class RouteEntry
    {
        public RouteEntry(Func<object?, Page> factory, bool cache)
        {
            Factory = factory;
            Cache = cache;
        }

        public Func<object?, Page> Factory { get; }
        public bool Cache { get; }
        public Page? CachedPage { get; set; }
    }

    private readonly ContentControl _mainHost;
    private readonly ContentControl _overlayHost;
    private readonly Dictionary<BigScreenRoute, RouteEntry> _routes = new();

    private Page? _mainPage;
    private Page? _overlayPage;
    private NavigationEntry? _currentMain;
    private readonly List<NavigationEntry> _backStack = new();
    private readonly List<NavigationEntry> _forwardStack = new();

    public BigScreenNavigator(ContentControl mainHost, ContentControl overlayHost)
    {
        _mainHost = mainHost;
        _overlayHost = overlayHost;
    }

    public bool IsOverlayOpen => _overlayHost.Visibility == Visibility.Visible;
    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;

    public void RequestFocusActivePage()
    {
        if (IsOverlayOpen && _overlayPage != null)
        {
            RequestFocus(_overlayPage);
            return;
        }

        if (_mainPage != null)
        {
            RequestFocus(_mainPage);
        }
    }

    public void Register(BigScreenRoute route, Func<object?, Page> factory, bool cache = true)
    {
        _routes[route] = new RouteEntry(factory, cache);
    }

    public void Navigate(BigScreenRoute route, object? parameter = null, BigScreenNavMode mode = BigScreenNavMode.Main, bool addToHistory = true)
    {
        if (!_routes.TryGetValue(route, out var entry))
            throw new InvalidOperationException($"Route not registered: {route}");

        if (mode == BigScreenNavMode.Main)
        {
            CloseOverlay(false);
            ShowMain(entry, route, parameter, addToHistory);
        }
        else
        {
            ShowOverlay(entry, parameter);
        }
    }

    public void CloseOverlay() => CloseOverlay(true);

    public bool GoBack()
    {
        if (!CanGoBack) return false;

        var current = _currentMain;
        if (current != null)
        {
            _forwardStack.Add(current);
        }

        var target = _backStack[^1];
        _backStack.RemoveAt(_backStack.Count - 1);
        Navigate(target.Route, target.Parameter, BigScreenNavMode.Main, false);
        return true;
    }

    public bool GoForward()
    {
        if (!CanGoForward) return false;

        var current = _currentMain;
        if (current != null)
        {
            _backStack.Add(current);
        }

        var target = _forwardStack[^1];
        _forwardStack.RemoveAt(_forwardStack.Count - 1);
        Navigate(target.Route, target.Parameter, BigScreenNavMode.Main, false);
        return true;
    }

    private void CloseOverlay(bool restoreFocus)
    {
        if (!IsOverlayOpen) return;

        NotifyNavigatedFrom(_overlayPage);
        _overlayHost.Content = null;
        _overlayHost.Visibility = Visibility.Collapsed;
        _mainHost.IsHitTestVisible = true;
        _overlayPage = null;

        if (restoreFocus && _mainPage != null)
        {
            RequestFocus(_mainPage);
        }
    }

    private void ShowMain(RouteEntry entry, BigScreenRoute route, object? parameter, bool addToHistory)
    {
        if (addToHistory && _currentMain != null)
        {
            _backStack.Add(_currentMain);
            _forwardStack.Clear();
        }

        var nextPage = ResolvePage(entry, parameter);
        if (!ReferenceEquals(_mainPage, nextPage))
        {
            NotifyNavigatedFrom(_mainPage);
            _mainPage = nextPage;
            _mainHost.Content = nextPage;
        }

        NotifyNavigatedTo(nextPage, parameter);
        RequestFocus(nextPage);
        _currentMain = new NavigationEntry(route, parameter);
    }

    private void ShowOverlay(RouteEntry entry, object? parameter)
    {
        var nextPage = ResolvePage(entry, parameter);
        if (!ReferenceEquals(_overlayPage, nextPage))
        {
            NotifyNavigatedFrom(_overlayPage);
            _overlayPage = nextPage;
            _overlayHost.Content = nextPage;
        }

        _overlayHost.Visibility = Visibility.Visible;
        _mainHost.IsHitTestVisible = false;

        NotifyNavigatedTo(nextPage, parameter);
        RequestFocus(nextPage);
    }

    private Page ResolvePage(RouteEntry entry, object? parameter)
    {
        if (!entry.Cache)
            return entry.Factory(parameter);

        entry.CachedPage ??= entry.Factory(parameter);
        return entry.CachedPage;
    }

    private static void NotifyNavigatedTo(Page page, object? parameter)
    {
        if (page is IBigScreenPage bigScreenPage)
        {
            bigScreenPage.OnNavigatedTo(parameter);
        }
    }

    private static void NotifyNavigatedFrom(Page? page)
    {
        if (page is IBigScreenPage bigScreenPage)
        {
            bigScreenPage.OnNavigatedFrom();
        }
    }

    private static void RequestFocus(Page page)
    {
        if (page is not IBigScreenPage bigScreenPage) return;

        if (page.IsLoaded)
        {
            bigScreenPage.RequestFocus();
            return;
        }

        RoutedEventHandler? handler = null;
        handler = (s, e) =>
        {
            page.Loaded -= handler;
            bigScreenPage.RequestFocus();
        };
        page.Loaded += handler;
    }

    private sealed record NavigationEntry(BigScreenRoute Route, object? Parameter);
}
