using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;

namespace PotatoVN.App.PluginBase.Views;

public enum InputDeviceType { Keyboard, Gamepad }

public static class InputManager
{
    public static event Action<InputDeviceType>? InputChanged;
    public static InputDeviceType CurrentInput { get; private set; } = InputDeviceType.Keyboard;

    public static void ReportInput(InputDeviceType type)
    {
        if (CurrentInput != type)
        {
            CurrentInput = type;
            InputChanged?.Invoke(type);
        }
    }
}

public class BigScreenHeader : Grid
{
    private TextBlock _clockBlock;
    private DispatcherTimer _timer;

    public BigScreenHeader()
    {
        Height = 60;
        Padding = new Thickness(40, 0, 40, 0);
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 255, 255, 255)); // Slight transparent bg

        _clockBlock = new TextBlock
        {
            FontSize = 20,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Children.Add(_clockBlock);

        // Timer for clock
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => UpdateTime();
        _timer.Start();
        UpdateTime();

        Unloaded += (s, e) => _timer.Stop();
    }

    private void UpdateTime()
    {
        _clockBlock.Text = DateTime.Now.ToString("HH:mm");
    }
}

public class BigScreenFooter : Grid
{
    private StackPanel _hintStack;

    public BigScreenFooter()
    {
        Height = 48;
        Padding = new Thickness(40, 0, 40, 0);
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 20, 20)); // Darker footer
        VerticalAlignment = VerticalAlignment.Bottom;

        _hintStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 20,
            VerticalAlignment = VerticalAlignment.Center
        };

        Children.Add(_hintStack);
    }

    public void UpdateHints(List<(string Key, string Action)> hints)
    {
        _hintStack.Children.Clear();
        foreach (var (key, action) in hints)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            
            // Key Badge (Visual representation of the button/key)
            var keyBorder = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                MinWidth = 24
            };
            
            var keyText = new TextBlock
            {
                Text = key,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            keyBorder.Child = keyText;

            // Action Label
            var actionText = new TextBlock
            {
                Text = action,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };

            item.Children.Add(keyBorder);
            item.Children.Add(actionText);
            
            _hintStack.Children.Add(item);
        }
    }
}
