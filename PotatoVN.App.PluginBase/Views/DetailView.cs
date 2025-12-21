using System;
using System.Collections.Generic;
using GalgameManager.Models;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PotatoVN.App.PluginBase.Models;
using PotatoVN.App.PluginBase.Services;
using Windows.System;
using Windows.UI;

namespace PotatoVN.App.PluginBase.Views;

public class DetailView : Grid
{
    private readonly Galgame _game;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;

    public DetailView(Galgame game)
    {
        _game = game;
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Padding = new Thickness(60, 20, 60, 40);

        if (game.HeaderImagePath.Value is string headerPath && !string.IsNullOrEmpty(headerPath))
        {
            Children.Add(new Image
            {
                Source = new BitmapImage(new Uri(headerPath)),
                Stretch = Stretch.UniformToFill,
                Opacity = 0.3
            });
        }

        Children.Add(new Grid
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 0),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop { Color = Color.FromArgb(255, 12, 15, 20), Offset = 0 },
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

        var playBtn = new Button
        {
            Content = Plugin.GetLocalized("BigScreen_Play") ?? "PLAY",
            FontSize = 24,
            Padding = new Thickness(60, 15, 60, 15),
            Background = new SolidColorBrush(Colors.Green),
            Foreground = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(4)
        };
        playBtn.Click += (s, e) => SimpleEventBus.Instance.Publish(new LaunchGameMessage(_game));

        // Handle Back button manually since we are in a sub-view
        playBtn.KeyDown += (s, e) =>
        {
            if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape)
            {
                SimpleEventBus.Instance.Publish(new NavigateToLibraryMessage());
                e.Handled = true;
            }
        };

        stack.Children.Add(playBtn);

        stack.Children.Add(new TextBlock { Text = game.Description.Value, FontSize = 18, Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), TextWrapping = TextWrapping.Wrap, MaxHeight = 200, TextTrimming = TextTrimming.CharacterEllipsis });

        Children.Add(stack);

        // Subscribe to input events
        SimpleEventBus.Instance.Subscribe<GamepadInputMessage>(OnGamepadInput);

        Loaded += (s, e) =>
        {
            playBtn.Focus(FocusState.Programmatic);
            PublishHints();
        };

        Unloaded += (s, e) => SimpleEventBus.Instance.Unsubscribe<GamepadInputMessage>(OnGamepadInput);

        GotFocus += (s, e) => PublishHints();
    }

    private void OnGamepadInput(GamepadInputMessage msg)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            // Only act if this view is effectively active (in visual tree)
            if (XamlRoot == null) return;

            switch (msg.Button)
            {
                case GamepadButton.A:
                    SimpleEventBus.Instance.Publish(new LaunchGameMessage(_game));
                    break;
                case GamepadButton.B:
                    SimpleEventBus.Instance.Publish(new NavigateToLibraryMessage());
                    break;
            }
        });
    }

    private void PublishHints()
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
