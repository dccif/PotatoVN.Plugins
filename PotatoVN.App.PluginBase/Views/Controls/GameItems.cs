using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using GalgameManager.Models;
using System;
using Microsoft.UI;
using Windows.UI;

namespace PotatoVN.App.PluginBase.Views.Controls;

public class RecentGameItem : Button
{
    private Galgame _game;
    private Image _portraitImg;
    private Image _landscapeImg;
    private TextBlock _titleBlock;
    private Grid _rootGrid;
    private Storyboard _expandSb;
    private Storyboard _collapseSb;

    private const double BaseWidth = 180;
    private const double ExpandedWidth = 320;
    private const double HeightVal = 260;

    public RecentGameItem(Galgame game)
    {
        _game = game;
        this.Width = BaseWidth;
        this.Height = HeightVal;
        this.Padding = new Thickness(0);
        this.BorderThickness = new Thickness(0);
        this.CornerRadius = new CornerRadius(8);
        this.Margin = new Thickness(6, 0, 6, 0);
        this.Background = new SolidColorBrush(Colors.Transparent);
        this.UseSystemFocusVisuals = false; // Disable default border
        
        // Disable default button visual states interfering heavily (optional, but Button is easiest for Focus/Click)

        _rootGrid = new Grid();
        
        // 1. Portrait Image (Default)
        _portraitImg = new Image
        {
            Stretch = Stretch.UniformToFill,
            Opacity = 1.0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!string.IsNullOrEmpty(game.ImagePath.Value))
            _portraitImg.Source = new BitmapImage(new Uri(game.ImagePath.Value));
            
        _rootGrid.Children.Add(_portraitImg);

        // 2. Landscape Image (Hidden initially)
        _landscapeImg = new Image
        {
            Stretch = Stretch.UniformToFill,
            Opacity = 0.0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!string.IsNullOrEmpty(game.HeaderImagePath.Value))
            _landscapeImg.Source = new BitmapImage(new Uri(game.HeaderImagePath.Value));
        else
             _landscapeImg.Source = _portraitImg.Source; // Fallback

        _rootGrid.Children.Add(_landscapeImg);

        // 3. Gradient & Text
        var overlay = new Grid
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 80,
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0.5, 0),
                EndPoint = new Windows.Foundation.Point(0.5, 1),
                GradientStops = { 
                    new GradientStop { Color = Colors.Transparent, Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(200, 0, 0, 0), Offset = 1 }
                }
            }
        };
        
        _titleBlock = new TextBlock
        {
            Text = game.Name.Value,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(12)
        };
        overlay.Children.Add(_titleBlock);
        _rootGrid.Children.Add(overlay);

        Content = _rootGrid;

        // Events
        GotFocus += OnGotFocus;
        LostFocus += OnLostFocus;
        
        // Init Animations
        InitializeAnimations();
    }

    private void InitializeAnimations()
    {
        // Expand
        _expandSb = new Storyboard();
        
        var widthAnim = new DoubleAnimation { To = ExpandedWidth, Duration = TimeSpan.FromMilliseconds(200), EnableDependentAnimation = true };
        Storyboard.SetTarget(widthAnim, this);
        Storyboard.SetTargetProperty(widthAnim, "Width");
        _expandSb.Children.Add(widthAnim);

        var fadeOutPortrait = new DoubleAnimation { To = 0.0, Duration = TimeSpan.FromMilliseconds(150) };
        Storyboard.SetTarget(fadeOutPortrait, _portraitImg);
        Storyboard.SetTargetProperty(fadeOutPortrait, "Opacity");
        _expandSb.Children.Add(fadeOutPortrait);

        var fadeInLandscape = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(fadeInLandscape, _landscapeImg);
        Storyboard.SetTargetProperty(fadeInLandscape, "Opacity");
        _expandSb.Children.Add(fadeInLandscape);

        // Collapse
        _collapseSb = new Storyboard();

        var widthAnimRev = new DoubleAnimation { To = BaseWidth, Duration = TimeSpan.FromMilliseconds(200), EnableDependentAnimation = true };
        Storyboard.SetTarget(widthAnimRev, this);
        Storyboard.SetTargetProperty(widthAnimRev, "Width");
        _collapseSb.Children.Add(widthAnimRev);

        var fadeInPortrait = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(fadeInPortrait, _portraitImg);
        Storyboard.SetTargetProperty(fadeInPortrait, "Opacity");
        _collapseSb.Children.Add(fadeInPortrait);

        var fadeOutLandscape = new DoubleAnimation { To = 0.0, Duration = TimeSpan.FromMilliseconds(150) };
        Storyboard.SetTarget(fadeOutLandscape, _landscapeImg);
        Storyboard.SetTargetProperty(fadeOutLandscape, "Opacity");
        _collapseSb.Children.Add(fadeOutLandscape);
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        // Bring to front (Z-Index) is handled by layout, but scaling/width works
        _collapseSb.Stop();
        _expandSb.Begin();
        
        // Scale Up slightly
        // Note: Simple transforms on Button usually require setting a TransformGroup first.
        // For now, Width expansion is the main effect requested.
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        _expandSb.Stop();
        _collapseSb.Begin();
    }
}

// Wrapper for GridView Items to handle Focus Animation
public class LibraryItemAnimationWrapper : Grid
{
    private ScaleTransform _scale;
    private SolidColorBrush _borderBrush;

    public LibraryItemAnimationWrapper()
    {
        _scale = new ScaleTransform { CenterX = 80, CenterY = 120 }; // Center of 160x240
        this.RenderTransform = _scale;
        
        // Border for focus highlight
        _borderBrush = new SolidColorBrush(Colors.Transparent);
        this.BorderBrush = _borderBrush;
        this.BorderThickness = new Thickness(2);
        this.CornerRadius = new CornerRadius(6);

        // We need to listen to the PARENT GridViewItem's focus, because this Grid is inside the template.
        // Or we can rely on PointEnter.
        // Actually, for GridView, the GridViewItem gets focus. 
        // We can use the Loaded event to find parent? No, tricky.
        // EASIER: Just handle PointerEntered/Exited for mouse.
        // FOR GAMEPAD/KEYBOARD: The GridViewItem gets focus. 
        // Since we can't easily modify GridViewItem style via string XAML, we will cheat:
        // We will make this Grid IsHitTestVisible=False? No.
        
        // Use DataBinding?
        // Let's rely on PointerEntered for mouse.
        // For Focus: We need to set IsTabStop=True on this Grid? No, GridViewItem wraps it.
        
        // Hack: Listen to EffectiveViewportChanged or LayoutUpdated? No.
        // Register to the Parents GotFocus?
        
        this.Loaded += (s, e) =>
        {
            // Find parent GridViewItem
            var parent = VisualTreeHelper.GetParent(this);
            while (parent != null && !(parent is GridViewItem))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is GridViewItem item)
            {
                item.GotFocus += (sender, args) => AnimateScale(1.1);
                item.LostFocus += (sender, args) => AnimateScale(1.0);
                item.PointerEntered += (sender, args) => AnimateScale(1.05);
                item.PointerExited += (sender, args) => 
                {
                    if (item.FocusState == FocusState.Unfocused) AnimateScale(1.0);
                    else AnimateScale(1.1);
                };
            }
        };
    }

    private void AnimateScale(double scale)
    {
        var sb = new Storyboard();
        
        var animX = new DoubleAnimation { To = scale, Duration = TimeSpan.FromMilliseconds(150), EnableDependentAnimation = true };
        Storyboard.SetTarget(animX, _scale);
        Storyboard.SetTargetProperty(animX, "ScaleX");
        
        var animY = new DoubleAnimation { To = scale, Duration = TimeSpan.FromMilliseconds(150), EnableDependentAnimation = true };
        Storyboard.SetTarget(animY, _scale);
        Storyboard.SetTargetProperty(animY, "ScaleY");

        sb.Children.Add(animX);
        sb.Children.Add(animY);
        sb.Begin();
        
        if (scale > 1.05) 
        {
             // Show Border/Highlight visual if heavily focused
             this.BorderBrush = new SolidColorBrush(Colors.White);
             Canvas.SetZIndex(this, 10); // Try to pop out
        }
        else
        {
             this.BorderBrush = new SolidColorBrush(Colors.Transparent);
             Canvas.SetZIndex(this, 0);
        }
    }
}