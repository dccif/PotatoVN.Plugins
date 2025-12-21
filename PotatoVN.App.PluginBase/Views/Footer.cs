using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PotatoVN.App.PluginBase.Models;
using PotatoVN.App.PluginBase.Services;
using Windows.UI;

namespace PotatoVN.App.PluginBase.Views;

public partial class Footer : Grid
{
    private readonly StackPanel _hintsPanel;
    private readonly DispatcherQueue _dispatcherQueue;

    public Footer()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        Padding = new Thickness(60, 10, 60, 15);
        Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));

        _hintsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 30,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Children.Add(_hintsPanel);

        // Subscribe to hint updates
        SimpleEventBus.Instance.Subscribe<UpdateHintsMessage>(OnUpdateHints);

        // Initial clean
        RenderHints(new List<HintAction>());

        Unloaded += (s, e) => SimpleEventBus.Instance.Unsubscribe<UpdateHintsMessage>(OnUpdateHints);
    }

    private void OnUpdateHints(UpdateHintsMessage msg)
    {
        // Marshaling to UI thread is required if the event comes from background
        if (_dispatcherQueue.HasThreadAccess)
        {
            RenderHints(msg.Hints);
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => RenderHints(msg.Hints));
        }
    }

    private void RenderHints(List<HintAction> hints)
    {
        _hintsPanel.Children.Clear();
        foreach (var hint in hints)
        {
            _hintsPanel.Children.Add(CreateHint(hint));
        }
    }

    private UIElement CreateHint(HintAction hint)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        bool isA = hint.Button.ToUpper() == "A";

        var border = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(isA ? Colors.Green : Colors.Red), // Simplified color logic
            Child = new TextBlock
            {
                Text = hint.Button,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            }
        };
        var textBlock = new TextBlock
        {
            Text = hint.Label,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14
        };
        stack.Children.Add(border);
        stack.Children.Add(textBlock);
        return stack;
    }
}
