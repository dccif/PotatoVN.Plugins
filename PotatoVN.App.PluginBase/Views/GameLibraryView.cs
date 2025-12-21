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

    public GameLibraryView(List<Galgame> games, Galgame? initialSelection = null)
    {
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        ItemsSource = games;
        ItemTemplate = GameItemTemplate.GetTemplate();
        SelectionMode = ListViewSelectionMode.Single;
        IsItemClickEnabled = true;
        Padding = new Thickness(60, 20, 60, 40);
        HorizontalAlignment = HorizontalAlignment.Center;
        XYFocusKeyboardNavigation = Microsoft.UI.Xaml.Input.XYFocusKeyboardNavigationMode.Enabled;
        IsTabStop = true;

        GotFocus += (s, e) =>
        {
            PublishHints();
            SyncSelectionToFocus(e.OriginalSource as DependencyObject);
        };

        ItemClick += (s, e) =>
        {
            if (e.ClickedItem is Galgame g)
            {
                SimpleEventBus.Instance.Publish(new NavigateToDetailMessage(g));
            }
        };

        // Backup KeyDown handler
        KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.GamepadB || e.Key == Windows.System.VirtualKey.Escape)
            {
                SimpleEventBus.Instance.Publish(new AppExitMessage());
                e.Handled = true;
            }
        };

        // Remove subscription from constructor
        // SimpleEventBus.Instance.Subscribe<GamepadInputMessage>(OnGamepadInput);

        Loaded += async (s, e) =>
        {
            // Subscribe on Load
            SimpleEventBus.Instance.Subscribe<GamepadInputMessage>(OnGamepadInput);

            await System.Threading.Tasks.Task.Delay(100);

            _dispatcherQueue.TryEnqueue(() =>
                    {
                        if (XamlRoot != null && games.Count > 0)
                        {
                            if (initialSelection != null && games.Contains(initialSelection))
                            {
                                SelectedItem = initialSelection;
                                ScrollIntoView(initialSelection);
                            }
                            else if (SelectedIndex < 0)
                            {
                                SelectedIndex = 0;
                            }

                            var targetItem = SelectedItem;
                            if (targetItem != null)
                            {
                                var container = ContainerFromItem(targetItem) as Control;
                                container?.Focus(FocusState.Programmatic);
                            }
                        }
                        if (XamlRoot != null) PublishHints();
                    });
        };

        Unloaded += (s, e) => SimpleEventBus.Instance.Unsubscribe<GamepadInputMessage>(OnGamepadInput);
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
