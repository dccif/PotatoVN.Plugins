using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PotatoVN.App.PluginBase.Models;
using PotatoVN.App.PluginBase.Services;
using Windows.UI;

namespace PotatoVN.App.PluginBase.Views;

public class Header : BigScreenViewBase
{
    private readonly TextBlock _clockText;
    private readonly TextBlock _sortText;
    private readonly Button _sortButton;
    private readonly DispatcherTimer _timer;
    private SortType _currentSort = SortType.LastPlayTime;
    private bool _isAscending = false;

    public Header()
    {
        _currentSort = Plugin.CurrentData.SortType;
        _isAscending = Plugin.CurrentData.SortAscending;

        this.IsTabStop = false;

        var grid = new Grid { Padding = new Thickness(60, 40, 60, 20) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "POTATO VN",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        };
        grid.Children.Add(title);

        _sortText = new TextBlock
        {
            FontSize = 18,
            Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))
        };

        _sortButton = new Button
        {
            Content = _sortText,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = null,
            IsTabStop = true,
            Margin = new Thickness(0, 0, 30, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _sortButton.Click += (s, e) => ToggleSort();
        UpdateSortText();

        grid.Children.Add(_sortButton);
        Grid.SetColumn(_sortButton, 1);

        _clockText = new TextBlock
        {
            FontSize = 24,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(_clockText);
        Grid.SetColumn(_clockText, 2);

        this.Content = grid;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => UpdateClock();
        _timer.Start();
        UpdateClock();

        this.Unloaded += (s, e) => _timer.Stop();
    }

    protected override bool IsActiveElement(object source) => source == _sortButton;

    private void ToggleSort()
    {
        if (_currentSort == SortType.LastPlayTime)
        {
            if (!_isAscending) _isAscending = true;
            else
            {
                _currentSort = SortType.AddTime;
                _isAscending = false;
            }
        }
        else // AddTime
        {
            if (!_isAscending) _isAscending = true;
            else
            {
                _currentSort = SortType.LastPlayTime;
                _isAscending = false;
            }
        }

        Plugin.CurrentData.SortType = _currentSort;
        Plugin.CurrentData.SortAscending = _isAscending;

        UpdateSortText();
        SimpleEventBus.Instance.Publish(new SortChangedMessage(_currentSort, _isAscending));
    }

    private void UpdateSortText()
    {
        string sortName = _currentSort == SortType.LastPlayTime ? (Plugin.GetLocalized("BigScreen_SortLastPlay") ?? "Last Play") : (Plugin.GetLocalized("BigScreen_SortAdded") ?? "Added");
        string arrow = _isAscending ? "\u2191" : "\u2193";
        _sortText.Text = $"{sortName} {arrow}";
    }

    private void UpdateClock() => _clockText.Text = DateTime.Now.ToString("HH:mm");

    public override void FocusDefaultElement() => _sortButton.Focus(FocusState.Programmatic);

    public override void OnGamepadInput(GamepadButton button)
    {
        if (button == GamepadButton.Y)
        {
            ToggleSort();
        }
    }

    public override void PublishHints()
    {
        SimpleEventBus.Instance.Publish(new UpdateHintsMessage(new List<HintAction> {
            new HintAction(Plugin.GetLocalized("BigScreen_Sort") ?? "Sort", "Y")
        }));
    }
}
