using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace PotatoVN.App.PluginBase.Views;

public class Header : Grid
{
    private readonly TextBlock _clockText;
    private readonly DispatcherTimer _timer;

    public Header()
    {
        Padding = new Thickness(60, 40, 60, 20);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "POTATO VN",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        };
        Children.Add(title);

        _clockText = new TextBlock
        {
            FontSize = 24,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Children.Add(_clockText);
        Grid.SetColumn(_clockText, 1);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => UpdateClock();
        _timer.Start();
        UpdateClock();

        Unloaded += (s, e) => _timer.Stop();
    }

    private void UpdateClock() => _clockText.Text = DateTime.Now.ToString("HH:mm");
}
