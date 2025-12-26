using GalgameManager.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PotatoVN.App.PluginBase.ViewModels;
using System.Collections.Generic;
using System.IO;
using Windows.UI;

namespace PotatoVN.App.PluginBase.Views;

public sealed class DetailPage : Page, IBigScreenPage
{
    public DetailViewModel? ViewModel { get; private set; }
    private Button _playBtn;
    private BigScreenFooter _footer;

    public DetailPage(Galgame game)
    {
        ViewModel = new DetailViewModel(game);

        var scaffold = new BigScreenScaffold();
        Content = scaffold;
        _footer = scaffold.Footer;

        // Content Container (Grid with BG)
        var contentContainer = new Grid();
        scaffold.Body = contentContainer;

        // Background Image (in Content Row)
        var bgImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            Opacity = 1.0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        contentContainer.Children.Add(bgImage);

        // Gradient Overlay
        var overlay = new Grid
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 0),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop { Color = Color.FromArgb(200, 12, 15, 20), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(0, 12, 15, 20), Offset = 1 }
                }
            }
        };
        contentContainer.Children.Add(overlay);

        // Main Content Stack
        var contentStack = new StackPanel
        {
            Spacing = 30,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 800,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(60, 20, 60, 40)
        };

        // Title
        var titleBlock = new TextBlock
        {
            FontSize = 56,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.Wrap
        };
        contentStack.Children.Add(titleBlock);

        // Meta Info (Developer, Rating)
        var metaStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };

        // Helper to create info items
        UIElement CreateInfo(string label, Binding valueBinding)
        {
            var s = new StackPanel();
            s.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)) });
            var v = new TextBlock { FontSize = 16, Foreground = new SolidColorBrush(Colors.White) };
            v.SetBinding(TextBlock.TextProperty, valueBinding);
            s.Children.Add(v);
            return s;
        }

        var devBinding = new Binding { Path = new PropertyPath("Game.Developer.Value"), Mode = BindingMode.OneWay };
        metaStack.Children.Add(CreateInfo("DEVELOPER", devBinding));

        var rateBinding = new Binding { Path = new PropertyPath("Game.Rating.Value"), Mode = BindingMode.OneWay };
        metaStack.Children.Add(CreateInfo("RATING", rateBinding));

        contentStack.Children.Add(metaStack);

        // Buttons
        var btnStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };

        _playBtn = new Button
        {
            Content = "PLAY",
            FontSize = 24,
            Padding = new Thickness(60, 15, 60, 15),
            Background = new SolidColorBrush(Colors.Green),
            Foreground = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(4)
        };
        btnStack.Children.Add(_playBtn);

        contentStack.Children.Add(btnStack);

        // Description
        var descBlock = new TextBlock
        {
            FontSize = 18,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 200,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        contentStack.Children.Add(descBlock);

        contentContainer.Children.Add(contentStack);

        // BINDINGS
        Loaded += (s, e) =>
        {
            RefreshHints(InputManager.CurrentInput);
            InputManager.InputChanged += RefreshHints;
            this.KeyDown += OnKeyDown;

            if (ViewModel == null) return;

            // Image
            var imgBinding = new Binding { Source = ViewModel, Path = new PropertyPath("Game.HeaderImagePath.Value"), Mode = BindingMode.OneWay };
            BindingOperations.SetBinding(bgImage, Image.SourceProperty, imgBinding);

            // Title
            var titleBinding = new Binding { Source = ViewModel, Path = new PropertyPath("Game.Name.Value"), Mode = BindingMode.OneWay };
            BindingOperations.SetBinding(titleBlock, TextBlock.TextProperty, titleBinding);

            // Desc
            var descBinding = new Binding { Source = ViewModel, Path = new PropertyPath("Game.Description.Value"), Mode = BindingMode.OneWay };
            BindingOperations.SetBinding(descBlock, TextBlock.TextProperty, descBinding);

            // Set DataContext for meta info
            contentContainer.DataContext = ViewModel;

            _playBtn.Command = ViewModel.PlayCommand;
            _playBtn.IsEnabled = IsPlayable(ViewModel.Game);

            // Initial Focus
            _playBtn.Focus(FocusState.Programmatic);
        };

        Unloaded += (s, args) =>
        {
            InputManager.InputChanged -= RefreshHints;
            this.KeyDown -= OnKeyDown;
        };
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isGamepad = e.Key >= Windows.System.VirtualKey.GamepadA && e.Key <= Windows.System.VirtualKey.GamepadRightThumbstickLeft;
        InputManager.ReportInput(isGamepad ? InputDeviceType.Gamepad : InputDeviceType.Keyboard);
    }

    private void RefreshHints(InputDeviceType type)
    {
        if (type == InputDeviceType.Gamepad)
        {
            _footer.UpdateHints(new List<(string, string)>
            {
                ("A", Plugin.GetLocalized("BigScreen_Launch") ?? "Launch"),
                ("B", Plugin.GetLocalized("BigScreen_Back") ?? "Back")
            });
        }
        else
        {
            _footer.UpdateHints(new List<(string, string)>
            {
                ("Enter", Plugin.GetLocalized("BigScreen_Launch") ?? "Launch"),
                ("Esc", Plugin.GetLocalized("BigScreen_Back") ?? "Back")
            });
        }
    }

    public void OnNavigatedTo(object? parameter)
    {
    }

    public void OnNavigatedFrom()
    {
    }

    public void RequestFocus()
    {
        _playBtn.Focus(FocusState.Programmatic);
    }

    private static bool IsPlayable(Galgame game)
    {
        if (string.IsNullOrWhiteSpace(game.ExePath)) return false;
        return File.Exists(game.ExePath);
    }
}
