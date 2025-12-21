using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;

namespace PotatoVN.App.PluginBase.Views;

public enum ViewKey
{
    Library,
    Detail,
    Settings
}

public class BigScreenNavigationService
{
    private readonly ContentControl _rootContainer;
    private readonly Dictionary<ViewKey, BigScreenViewBase> _cache = new();
    private readonly Func<ViewKey, object?, BigScreenViewBase> _viewFactory;

    public BigScreenNavigationService(ContentControl rootContainer,
        Func<ViewKey, object?, BigScreenViewBase> viewFactory)
    {
        _rootContainer = rootContainer;
        _viewFactory = viewFactory;
    }

    public BigScreenViewBase? CurrentView { get; private set; }

    public void NavigateTo(ViewKey key, object? parameter = null)
    {
        // 1. Cleanup old view
        if (_rootContainer.Content is IBigScreenView oldView)
        {
            oldView.OnNavigatedFrom();
        }

        // 2. Get or create new view
        BigScreenViewBase nextView;

        // Cache logic for Library, but Detail is usually transient or we can update it
        if (key == ViewKey.Library)
        {
            if (!_cache.TryGetValue(key, out nextView))
            {
                nextView = _viewFactory(key, parameter);
                _cache[key] = nextView;
            }
        }
        else
        {
            nextView = _viewFactory(key, parameter);
        }

        CurrentView = nextView;

        // 3. Switch Content and trigger logic
        _rootContainer.Content = nextView;
        nextView.OnNavigatedTo(parameter);

        // 4. Focus Optimization - Delayed to ensure visual tree is ready
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(async () =>
        {
            await System.Threading.Tasks.Task.Delay(100);
            nextView.FocusDefaultElement();
        });
    }
}