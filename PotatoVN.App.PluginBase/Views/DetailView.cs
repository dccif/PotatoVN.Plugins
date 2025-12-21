using System;
using System.Collections.Generic;
using GalgameManager.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PotatoVN.App.PluginBase.Models;
using PotatoVN.App.PluginBase.Services;
using Windows.System;
using Windows.UI;

namespace PotatoVN.App.PluginBase.Views;

public class DetailView : BigScreenViewBase
{
    private readonly Galgame _game;
    private Button? _playBtn;

    public DetailView(Galgame game)
    {
        _game = game;

        var rootGrid = new Grid { Padding = new Thickness(60, 20, 60, 40) };

        if (game.HeaderImagePath.Value is string headerPath && !string.IsNullOrEmpty(headerPath))
        {
            rootGrid.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri(headerPath)),
                Stretch = Stretch.UniformToFill,
                Opacity = 1.0
            });
        }

        rootGrid.Children.Add(new Grid
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
        });

        var stack = new StackPanel { Spacing = 30, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Left };

        stack.Children.Add(new TextBlock { Text = game.Name.Value, FontSize = 56, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Colors.White), TextWrapping = TextWrapping.Wrap });

        var infoStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        infoStack.Children.Add(CreateInfoItem(Plugin.GetLocalized("BigScreen_Developer") ?? "DEVELOPER", game.Developer.Value!));
        infoStack.Children.Add(CreateInfoItem(Plugin.GetLocalized("BigScreen_Rating") ?? "RATING", game.Rating.Value.ToString("F1")));
        infoStack.Children.Add(CreateInfoItem(Plugin.GetLocalized("BigScreen_Release") ?? "RELEASE", game.ReleaseDate.Value.ToShortDateString()));

        string playTimeStr = game.TotalPlayTime < 60 ? $"{game.TotalPlayTime} M" : $"{(game.TotalPlayTime / 60.0):F1} H";
        infoStack.Children.Add(CreateInfoItem(Plugin.GetLocalized("BigScreen_PlayTime") ?? "PLAY TIME", playTimeStr));
        stack.Children.Add(infoStack);

        _playBtn = new Button
        {
            Content = Plugin.GetLocalized("BigScreen_Play") ?? "PLAY",
            FontSize = 24,
            Padding = new Thickness(60, 15, 60, 15),
            Background = new SolidColorBrush(Colors.Green),
            Foreground = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(4),
            IsEnabled = !string.IsNullOrEmpty(game.ExePath),
            IsTabStop = true
        };
        _playBtn.Click += (s, e) => SimpleEventBus.Instance.Publish(new LaunchGameMessage(_game));

        // Handle Back button manually since we are in a sub-view
        _playBtn.KeyDown += (s, e) =>
        {
            if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape)
            {
                SimpleEventBus.Instance.Publish(new NavigateToLibraryMessage());
                e.Handled = true;
            }
        };

        stack.Children.Add(_playBtn);

        stack.Children.Add(new TextBlock { Text = game.Description.Value, FontSize = 18, Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), TextWrapping = TextWrapping.Wrap, MaxHeight = 200, TextTrimming = TextTrimming.CharacterEllipsis });

        rootGrid.Children.Add(stack);
        this.Content = rootGrid;
    }

    public override void OnNavigatedTo(object? parameter)
    {
        base.OnNavigatedTo(parameter);
        _dispatcherQueue.TryEnqueue(async () =>
        {
            await System.Threading.Tasks.Task.Delay(50);
            _playBtn?.Focus(FocusState.Programmatic);
            PublishHints();
        });
    }

    public override void OnGamepadInput(GamepadButton button)
    {
        switch (button)
        {
            case GamepadButton.A:
                SimpleEventBus.Instance.Publish(new LaunchGameMessage(_game));
                break;
            case GamepadButton.B:
                SimpleEventBus.Instance.Publish(new NavigateToLibraryMessage());
                break;
        }
    }

    public override void PublishHints()
    {
        SimpleEventBus.Instance.Publish(new UpdateHintsMessage(new List<HintAction>
        {
            new HintAction(Plugin.GetLocalized("BigScreen_Launch") ?? "LAUNCH", "A"),
            new HintAction(Plugin.GetLocalized("BigScreen_Back") ?? "BACK", "B")
        }));
    }

    private UIElement CreateInfoItem(string label, string val)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)) });
        stack.Children.Add(new TextBlock { Text = val, FontSize = 16, Foreground = new SolidColorBrush(Colors.White) });
        return stack;
    }
}
