using System.Collections.Generic;
using GalgameManager.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PotatoVN.App.PluginBase.Models;
using PotatoVN.App.PluginBase.Services;
using PotatoVN.App.PluginBase.Templates;

namespace PotatoVN.App.PluginBase.Views;

public class GameLibraryView : BigScreenViewBase
{
    public GridView GridViewInner { get; private set; }
    private Galgame? _initialSelection;

    public GameLibraryView(List<Galgame> games, Galgame? initialSelection = null)
    {
        _initialSelection = initialSelection;

        // Initial Sort
        var sortType = Plugin.CurrentData.SortType;
        var ascending = Plugin.CurrentData.SortAscending;
        SortGames(games, sortType, ascending);

        GridViewInner = new GridView
        {
            ItemsSource = games,
            ItemTemplate = GameItemTemplate.GetTemplate(),
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            Padding = new Thickness(60, 20, 60, 40),
            HorizontalAlignment = HorizontalAlignment.Center,
            // 核心：禁用原生导航防止 AccessViolationException
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Disabled,
            IsTabStop = true
        };

        GridViewInner.GotFocus += (s, e) =>
        {
            PublishHints();
            SyncSelectionToFocus(e.OriginalSource as DependencyObject);
        };

        GridViewInner.ItemClick += (s, e) =>
        {
            if (e.ClickedItem is Galgame g)
            {
                SimpleEventBus.Instance.Publish(new NavigateToDetailMessage(g));
            }
        };

        // Backup KeyDown handler
        GridViewInner.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.GamepadB || e.Key == Windows.System.VirtualKey.Escape)
            {
                SimpleEventBus.Instance.Publish(new AppExitMessage());
                e.Handled = true;
            }
        };

        Content = GridViewInner;

        Loaded += (s, e) => SimpleEventBus.Instance.Subscribe<SortChangedMessage>(OnSortChanged);
        Unloaded += (s, e) => SimpleEventBus.Instance.Unsubscribe<SortChangedMessage>(OnSortChanged);
    }

    private void SortGames(List<Galgame> games, SortType sortType, bool ascending)
    {
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
    }

    public override void FocusDefaultElement()
    {
        var targetItem = GridViewInner.SelectedItem;
        if (targetItem != null)
        {
            var container = GridViewInner.ContainerFromItem(targetItem) as Control;
            (container ?? GridViewInner).Focus(FocusState.Programmatic);
        }
        else
        {
            GridViewInner.Focus(FocusState.Programmatic);
        }
    }

    public override void OnNavigatedTo(object? parameter)
    {
        base.OnNavigatedTo(parameter);

        if (parameter is Galgame g)
        {
            _initialSelection = g;
        }

        _dispatcherQueue.TryEnqueue(async () =>
        {
            await System.Threading.Tasks.Task.Delay(50);

            if (GridViewInner.Items.Count > 0)
            {
                var games = GridViewInner.ItemsSource as List<Galgame>;

                // Try to restore selection
                if (games != null && _initialSelection != null && games.Contains(_initialSelection))
                {
                    GridViewInner.SelectedItem = _initialSelection;
                    GridViewInner.ScrollIntoView(_initialSelection);
                }
                else if (GridViewInner.SelectedIndex < 0)
                {
                    GridViewInner.SelectedIndex = 0;
                }

                FocusDefaultElement();
            }
        });
    }

    private void OnSortChanged(SortChangedMessage msg)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var selected = GridViewInner.SelectedItem as Galgame;
            var games = GridViewInner.ItemsSource as List<Galgame>;
            if (games == null) return;

            SortGames(games, msg.Type, msg.Ascending);

            // Refresh View
            GridViewInner.ItemsSource = null;
            GridViewInner.ItemsSource = games;

            if (selected != null)
            {
                GridViewInner.SelectedItem = selected;
                GridViewInner.ScrollIntoView(selected);
                FocusDefaultElement();
            }
        });
    }

    private void SyncSelectionToFocus(DependencyObject? originalSource)
    {
        var item = originalSource;
        while (item != null && !(item is GridViewItem) && item != GridViewInner)
        {
            item = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(item);
        }

        if (item is GridViewItem container && container.Content is Galgame g)
        {
            GridViewInner.SelectedItem = g;
        }
    }

    public override void OnGamepadInput(GamepadButton button)
    {
        switch (button)
        {
            case GamepadButton.A:
                if (GridViewInner.SelectedItem is Galgame g)
                {
                    SimpleEventBus.Instance.Publish(new NavigateToDetailMessage(g));
                }
                break;
            case GamepadButton.B:
                SimpleEventBus.Instance.Publish(new AppExitMessage());
                break;

            case GamepadButton.Up:
                MoveVerticalFocus(FocusNavigationDirection.Up, button);
                break;
            case GamepadButton.Down:
                MoveVerticalFocus(FocusNavigationDirection.Down, button);
                break;
            case GamepadButton.Left:
                if (GridViewInner.SelectedIndex > 0)
                {
                    MoveSelection(-1);
                }
                break;
            case GamepadButton.Right:
                if (GridViewInner.Items.Count > 0 && GridViewInner.SelectedIndex < GridViewInner.Items.Count - 1)
                {
                    MoveSelection(1);
                }
                break;
        }
    }

    private void MoveVerticalFocus(FocusNavigationDirection direction, GamepadButton originalButton)
    {
        // 1. Try local move within GridViewInner (handles dynamic columns natively)
        var options = new FindNextElementOptions
        {
            SearchRoot = GridViewInner,
            XYFocusNavigationStrategyOverride = XYFocusNavigationStrategyOverride.Projection
        };

        var moved = FocusManager.TryMoveFocus(direction, options);

        // 2. If local move failed (boundary), try global move to escape (e.g. to Header)
        if (!moved && XamlRoot?.Content is DependencyObject root)
        {
            options.SearchRoot = root;
            moved = FocusManager.TryMoveFocus(direction, options);
        }

        if (!moved)
        {
            SimpleEventBus.Instance.Publish(new UnhandledGamepadInputMessage(originalButton));
        }
    }

    private void MoveSelection(int delta)
    {
        int nextIndex = GridViewInner.SelectedIndex + delta;
        if (nextIndex >= 0 && nextIndex < GridViewInner.Items.Count)
        {
            GridViewInner.SelectedIndex = nextIndex;
            GridViewInner.ScrollIntoView(GridViewInner.SelectedItem);
            FocusDefaultElement();
        }
    }

    public override void PublishHints()
    {
        SimpleEventBus.Instance.Publish(new UpdateHintsMessage(new List<HintAction>
        {
            new HintAction(Plugin.GetLocalized("BigScreen_Select") ?? "SELECT", "A"),
            new HintAction(Plugin.GetLocalized("BigScreen_Exit") ?? "EXIT", "B")
        }));
    }
}
