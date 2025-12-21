using System;
using System.Collections.Generic;
using GalgameManager.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.PluginBase.Models;
using PotatoVN.App.PluginBase.Services;
using PotatoVN.App.PluginBase.Templates;

namespace PotatoVN.App.PluginBase.Views;

public class GameLibraryView : GridView
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private bool _isFirstLoad = true;

    public GameLibraryView(List<Galgame> games, Galgame? initialSelection = null)
    {
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        
        // Initial Sort
        var sortType = Plugin.CurrentData.SortType;
        var ascending = Plugin.CurrentData.SortAscending;
        if (sortType == SortType.LastPlayTime)
        {
            if (ascending) games.Sort((a, b) => a.LastPlayTime.CompareTo(b.LastPlayTime));
            else games.Sort((a, b) => b.LastPlayTime.CompareTo(a.LastPlayTime));
        }
        else // AddTime
        {
            if (ascending) games.Sort((a, b) => a.AddTime.CompareTo(b.AddTime));
            else games.Sort((a, b) => b.AddTime.CompareTo(a.AddTime));
        }

        this.ItemsSource = games;
        this.ItemTemplate = GameItemTemplate.GetTemplate();
        this.SelectionMode = ListViewSelectionMode.Single;
        this.IsItemClickEnabled = true;
        this.Padding = new Thickness(60, 20, 60, 40);
        this.HorizontalAlignment = HorizontalAlignment.Center;
        this.XYFocusKeyboardNavigation = Microsoft.UI.Xaml.Input.XYFocusKeyboardNavigationMode.Enabled;
        this.IsTabStop = true;

        this.GotFocus += (s, e) => 
        {
            PublishHints();
            SyncSelectionToFocus(e.OriginalSource as DependencyObject);
        };
        
        this.ItemClick += (s, e) =>
        {
            if (e.ClickedItem is Galgame g)
            {
                SimpleEventBus.Instance.Publish(new NavigateToDetailMessage(g));
            }
        };
        
        // Backup KeyDown handler
        this.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.GamepadB || e.Key == Windows.System.VirtualKey.Escape)
            {
                SimpleEventBus.Instance.Publish(new AppExitMessage());
                e.Handled = true;
            }
        };
        
        // Remove subscription from constructor
        // SimpleEventBus.Instance.Subscribe<GamepadInputMessage>(OnGamepadInput);

        this.Loaded += async (s, e) =>
        {
             // Subscribe on Load
             SimpleEventBus.Instance.Subscribe<GamepadInputMessage>(OnGamepadInput);
             SimpleEventBus.Instance.Subscribe<SortChangedMessage>(OnSortChanged);

             if (_isFirstLoad)
             {
                 await System.Threading.Tasks.Task.Delay(100);
                 
                 _dispatcherQueue.TryEnqueue(() => 
                 {
                     if (this.XamlRoot != null && games.Count > 0)
                     {
                         if (initialSelection != null && games.Contains(initialSelection))
                         {
                             this.SelectedItem = initialSelection;
                             this.ScrollIntoView(initialSelection);
                         }
                         else if (this.SelectedIndex < 0)
                         {
                             this.SelectedIndex = 0;
                         }
                         
                         var targetItem = this.SelectedItem;
                         if (targetItem != null)
                         {
                            var container = this.ContainerFromItem(targetItem) as Control;
                            container?.Focus(FocusState.Programmatic);
                         }
                     }
                     if (this.XamlRoot != null) PublishHints();
                 });
                 _isFirstLoad = false;
             }
            else
            {
                // On subsequent loads (returning from Detail), just ensure focus is restored to the selected item if lost
                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (XamlRoot != null)
                    {
                        var targetItem = SelectedItem;
                        if (targetItem != null)
                        {
                            var container = ContainerFromItem(targetItem) as Control;
                            container?.Focus(FocusState.Programmatic);
                        }
                        PublishHints();
                    }
                });
            }
        };

        Unloaded += (s, e) =>
        {
            SimpleEventBus.Instance.Unsubscribe<GamepadInputMessage>(OnGamepadInput);
            SimpleEventBus.Instance.Unsubscribe<SortChangedMessage>(OnSortChanged);
        };
    }

    private void OnSortChanged(SortChangedMessage msg)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var selected = SelectedItem as Galgame;
            var games = ItemsSource as List<Galgame>;
            if (games == null) return;

            if (msg.Type == SortType.LastPlayTime)
            {
                if (msg.Ascending) games.Sort((a, b) => a.LastPlayTime.CompareTo(b.LastPlayTime));
                else games.Sort((a, b) => b.LastPlayTime.CompareTo(a.LastPlayTime));
            }
            else // AddTime
            {
                if (msg.Ascending) games.Sort((a, b) => a.AddTime.CompareTo(b.AddTime));
                else games.Sort((a, b) => b.AddTime.CompareTo(a.AddTime));
            }

            // Refresh View
            ItemsSource = null;
            ItemsSource = games;

            if (selected != null)
            {
                SelectedItem = selected;
                ScrollIntoView(selected);

                // Refocus
                var container = ContainerFromItem(selected) as Control;
                container?.Focus(FocusState.Programmatic);
            }
        });
    }
    private void SyncSelectionToFocus(DependencyObject? originalSource)
    {
        var item = originalSource;
        while (item != null && !(item is GridViewItem) && item != this)
        {
            item = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(item);
        }

        if (item is GridViewItem container && container.Content is Galgame g)
        {
            SelectedItem = g;
        }
    }

    private void OnGamepadInput(GamepadInputMessage msg)
    {
        _dispatcherQueue.TryEnqueue(async () =>
        {
            // Only act if this view is effectively active (in visual tree)
            if (XamlRoot == null) return;

            try
            {
                switch (msg.Button)
                {
                    case GamepadButton.A:
                        if (SelectedItem is Galgame g)
                        {
                            SimpleEventBus.Instance.Publish(new NavigateToDetailMessage(g));
                        }
                        else if (Items.Count > 0)
                        {
                            SelectedIndex = 0;
                            if (SelectedItem is Galgame first)
                                SimpleEventBus.Instance.Publish(new NavigateToDetailMessage(first));
                        }
                        break;
                    case GamepadButton.B:
                        SimpleEventBus.Instance.Publish(new AppExitMessage());
                        break;
                    case GamepadButton.Up:
                        await MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Up);
                        break;
                    case GamepadButton.Down:
                        await MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Down);
                        break;
                    case GamepadButton.Left:
                        await MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Left);
                        break;
                    case GamepadButton.Right:
                        await MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Right);
                        break;
                }
            }
            catch { }
        });
    }

    private async System.Threading.Tasks.Task MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection direction)
    {
        if (XamlRoot?.Content is DependencyObject root)
        {
            var options = new Microsoft.UI.Xaml.Input.FindNextElementOptions { SearchRoot = root };
            await Microsoft.UI.Xaml.Input.FocusManager.TryMoveFocusAsync(direction, options);
        }
    }

    public void PublishHints()
    {
        SimpleEventBus.Instance.Publish(new UpdateHintsMessage(new List<HintAction>
        {
            new HintAction(Plugin.GetLocalized("BigScreen_Select") ?? "SELECT", "A"),
            new HintAction(Plugin.GetLocalized("BigScreen_Exit") ?? "EXIT", "B")
        }));
    }
}
