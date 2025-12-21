using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PotatoVN.App.PluginBase.Models;
using PotatoVN.App.PluginBase.Services;
using Windows.UI;

namespace PotatoVN.App.PluginBase.Views;

public class Header : Grid
{
    private readonly TextBlock _clockText;
    private readonly TextBlock _sortText;
    private readonly DispatcherTimer _timer;
    private SortType _currentSort = SortType.LastPlayTime;
    private bool _isAscending = false;

    public Header()
    {
        _currentSort = Plugin.CurrentData.SortType;
        _isAscending = Plugin.CurrentData.SortAscending;

        Padding = new Thickness(60, 40, 60, 20);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "POTATO VN",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        };
        Children.Add(title);

        _sortText = new TextBlock
        {
            FontSize = 18,
            Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 30, 0),
            IsHitTestVisible = true
        };
        _sortText.PointerPressed += (s, e) => ToggleSort();
        UpdateSortText();
        Children.Add(_sortText);
        Grid.SetColumn(_sortText, 1);

        _clockText = new TextBlock
        {
            FontSize = 24,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Children.Add(_clockText);
        Grid.SetColumn(_clockText, 2);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => UpdateClock();
        _timer.Start();
        UpdateClock();

        Unloaded += (s, e) => _timer.Stop();
    }

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
}
