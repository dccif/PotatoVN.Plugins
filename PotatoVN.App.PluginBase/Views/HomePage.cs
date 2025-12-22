using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.PluginBase.ViewModels;
using GalgameManager.Models;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using System;
using PotatoVN.App.PluginBase.Views.Controls;
using Windows.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Numerics;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI;

namespace PotatoVN.App.PluginBase.Views;

public sealed class HomePage : Page, IBigScreenPage
{
    public HomeViewModel? ViewModel { get; private set; }
    
    // UI Elements
    private StackPanel _recentListPanel;
    private GridView _libraryGridView;
    private BigScreenFooter _footer;
    private ScrollViewer _mainScroll;
    private ScrollViewer _recentScroll;
    private Canvas _libraryOverlayCanvas;
    private Border? _libraryOverlayCard;
    private Image? _libraryOverlayImage;
    private TextBlock? _libraryOverlayTitle;
    private GridViewItem? _overlayItem;
    private ScaleTransform? _overlayScale;
    private ItemsWrapGrid? _libraryItemsPanel;

    private const double LibraryCardWidth = 160;
    private const double LibraryCardMargin = 6;
    private const double LibraryAspect = 1.5;
    private const double LibraryHorizontalPadding = 60;
    private const double LibraryBottomPadding = 40;

    private Control? _lastFocusedControl;

    public HomePage(List<Galgame> games)
    {
        ViewModel = new HomeViewModel(games);
        this.XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled;

        var scaffold = new BigScreenScaffold();
        Content = scaffold;
        _footer = scaffold.Footer;

        // Main Content ScrollViewer (Vertical)
        _mainScroll = new ScrollViewer 
        { 
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsTabStop = false // Important: Don't take focus
        };

        _libraryOverlayCanvas = new Canvas
        {
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(_libraryOverlayCanvas, 100);

        var bodyGrid = new Grid();
        bodyGrid.Children.Add(_mainScroll);
        scaffold.Body = bodyGrid;

        var contentStack = new StackPanel { Padding = new Thickness(0, 20, 0, 40) };
        var scrollContent = new Grid();
        scrollContent.Children.Add(contentStack);
        scrollContent.Children.Add(_libraryOverlayCanvas);
        _mainScroll.Content = scrollContent;

        // --- Recent Games Section ---
        var recentHeader = new TextBlock
        {
            Text = Plugin.GetLocalized("BigScreen_Recent") ?? "RECENT",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
            Margin = new Thickness(60, 0, 0, 10)
        };
        contentStack.Children.Add(recentHeader);

        _recentScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Padding = new Thickness(54, 0, 60, 20),
            IsTabStop = false // Don't take focus
        };
        
        _recentListPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled,
            XYFocusDownNavigationStrategy = XYFocusNavigationStrategy.Projection
        };
        _recentScroll.Content = _recentListPanel;
        contentStack.Children.Add(_recentScroll);

        // --- Library Section ---
        var libraryHeader = new TextBlock
        {
            Text = Plugin.GetLocalized("BigScreen_Library") ?? "LIBRARY",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
            Margin = new Thickness(60, 20, 0, 10)
        };
        contentStack.Children.Add(libraryHeader);

        _libraryGridView = new GridView
        {
            Padding = new Thickness(LibraryHorizontalPadding, 0, LibraryHorizontalPadding, LibraryBottomPadding),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectionMode = ListViewSelectionMode.None, // Prevent persistent selection state (ghosting)
            IsItemClickEnabled = true,
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled,
            XYFocusUpNavigationStrategy = XYFocusNavigationStrategy.Projection
        };
        _libraryGridView.ItemContainerStyle = BuildLibraryItemContainerStyle();
        contentStack.Children.Add(_libraryGridView);

        string libraryTemplate = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
    <Grid MinWidth='160' MinHeight='240' Margin='6' Background='#2d3440' CornerRadius='6'>
        <Grid.RowDefinitions>
            <RowDefinition Height='*' />
            <RowDefinition Height='Auto' />
        </Grid.RowDefinitions>
        
        <Image Source='{Binding ImagePath.Value}' 
               Stretch='UniformToFill' 
               HorizontalAlignment='Center' 
               VerticalAlignment='Center'/>
        
        <Grid Grid.Row='0' Grid.RowSpan='2' VerticalAlignment='Bottom' Height='60'>
            <Grid.Background>
                <LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'>
                    <GradientStop Color='#00000000' Offset='0'/>
                    <GradientStop Color='#CC000000' Offset='1'/>
                </LinearGradientBrush>
            </Grid.Background>
        </Grid>

        <TextBlock Grid.Row='1' 
                   Text='{Binding Name.Value}' 
                   Foreground='White'
                   FontSize='13'
                   FontWeight='Medium'
                   TextTrimming='CharacterEllipsis'
                   TextWrapping='NoWrap'
                   Margin='8,0,8,8'
                   VerticalAlignment='Bottom'/>
    </Grid>
</DataTemplate>";

        try
        {
            _libraryGridView.ItemTemplate = (DataTemplate)XamlReader.Load(libraryTemplate);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HomePage] Library Template Load Failed: {ex}");
        }

        // Logic
        PopulateRecentGames();
        UpdateFocusMap();
        
        var binding = new Binding { Source = ViewModel, Path = new PropertyPath("LibraryGames"), Mode = BindingMode.OneWay };
        BindingOperations.SetBinding(_libraryGridView, ItemsControl.ItemsSourceProperty, binding);

        _libraryGridView.ItemClick += (s, e) => 
        {
             if (e.ClickedItem is Galgame g && ViewModel != null) ViewModel.ItemClickCommand.Execute(g);
        };

        _libraryGridView.ContainerContentChanging += (s, e) =>
        {
            if (e.ItemContainer is GridViewItem item)
            {
                AttachLibraryItemOverlay(item);
                item.KeyDown -= OnLibraryItemKeyDown;
                item.KeyDown += OnLibraryItemKeyDown;
            }
        };
        _libraryGridView.Loaded += (s, e) => UpdateLibraryItemSize();
        _libraryGridView.SizeChanged += (s, e) => UpdateLibraryItemSize();
        // Overlay position is synced via CompositionTarget.Rendering while focused.
        if (XamlRoot != null)
        {
            XamlRoot.Changed += (s, e) => UpdateLibraryItemSize();
        }
        
        // Auto-center library items vertically
        _libraryGridView.GotFocus += (s, e) =>
        {
            if (e.OriginalSource is FrameworkElement item)
            {
                CenterVerticalInMainScroll(item);
            }
        };

        // Track Focus
        this.GotFocus += (s, e) =>
        {
            if (e.OriginalSource is Control control)
            {
                _lastFocusedControl = control;
            }
        };

        // Events
        Loaded += async (s, args) =>
        {
            RefreshHints(InputManager.CurrentInput);
            InputManager.InputChanged += RefreshHints;
            this.KeyDown += OnKeyDown;

            await System.Threading.Tasks.Task.Delay(100);
            
            if (_lastFocusedControl == null)
            {
                FocusInitial();
            }
        };

        Unloaded += (s, args) =>
        {
            InputManager.InputChanged -= RefreshHints;
            this.KeyDown -= OnKeyDown;
        };
    }

    public void OnNavigatedTo(object? parameter)
    {
    }

    public void OnNavigatedFrom()
    {
    }

    public void RequestFocus()
    {
        RestoreFocus();
    }

    public void RestoreFocus()
    {
        if (_lastFocusedControl != null)
        {
            _lastFocusedControl.Focus(FocusState.Programmatic);
            // Ensure visibility if it was scrolled out?
            // Usually if it wasn't unloaded, scroll pos is same.
            // But if window resized or something, checking center is safe.
            if (IsDescendantOf(_lastFocusedControl, _recentListPanel))
            {
                CenterInScrollViewer(_recentScroll, _lastFocusedControl);
                // Also ensure Main scroll is top
                _mainScroll.ChangeView(null, 0, null);
            }
            else if (IsDescendantOf(_lastFocusedControl, _libraryGridView))
            {
                CenterVerticalInMainScroll(_lastFocusedControl);
            }
        }
        else
        {
            FocusInitial();
        }
    }

    private void PopulateRecentGames()
    {
        if (ViewModel == null) return;
        foreach (var game in ViewModel.RecentGames)
        {
            var item = new RecentGameItem(game);
            item.Click += (s, e) => ViewModel.ItemClickCommand.Execute(game);
            item.GotFocus += (s, e) => CenterInScrollViewer(_recentScroll, item);
            item.KeyDown -= OnRecentItemKeyDown;
            item.KeyDown += OnRecentItemKeyDown;
            _recentListPanel.Children.Add(item);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isGamepad = e.Key >= Windows.System.VirtualKey.GamepadA && e.Key <= Windows.System.VirtualKey.GamepadRightThumbstickLeft;
        InputManager.ReportInput(isGamepad ? InputDeviceType.Gamepad : InputDeviceType.Keyboard);
    }

    private bool IsDescendantOf(DependencyObject? node, DependencyObject parent)
    {
        while (node != null)
        {
            if (node == parent) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private void FocusRecentItem(int index)
    {
        if (index < 0 || index >= _recentListPanel.Children.Count) return;
        
        var target = _recentListPanel.Children[index] as Control;
        if (target != null)
        {
            target.Focus(FocusState.Programmatic);
            CenterInScrollViewer(_recentScroll, target);
        }
    }

    private void OnRecentItemKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!IsDownKey(e.Key)) return;

        if (sender is FrameworkElement element)
        {
            if (FocusNearestLibraryItem(element))
            {
                e.Handled = true;
            }
        }
    }

    private void OnLibraryItemKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!IsUpKey(e.Key)) return;

        if (sender is GridViewItem item && IsAtTopRow(item))
        {
            if (FocusNearestRecentItem(item))
            {
                e.Handled = true;
            }
        }
    }

    private void AttachLibraryItemOverlay(GridViewItem item)
    {
        item.UseSystemFocusVisuals = false;
        item.GotFocus -= OnLibraryItemGotFocus;
        item.GotFocus += OnLibraryItemGotFocus;
        item.LostFocus -= OnLibraryItemLostFocus;
        item.LostFocus += OnLibraryItemLostFocus;
        item.Unloaded -= OnLibraryItemUnloaded;
        item.Unloaded += OnLibraryItemUnloaded;
    }

    private void OnLibraryItemGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is GridViewItem item)
        {
            ShowLibraryOverlay(item);
            item.LayoutUpdated -= OnLibraryItemLayoutUpdated;
            item.LayoutUpdated += OnLibraryItemLayoutUpdated;
        }
    }

    private void OnLibraryItemLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is GridViewItem item && _overlayItem == item)
        {
            item.LayoutUpdated -= OnLibraryItemLayoutUpdated;
            HideLibraryOverlay();
        }
    }

    private void OnLibraryItemUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is GridViewItem item)
        {
            item.GotFocus -= OnLibraryItemGotFocus;
            item.LostFocus -= OnLibraryItemLostFocus;
            item.Unloaded -= OnLibraryItemUnloaded;
            item.LayoutUpdated -= OnLibraryItemLayoutUpdated;
            if (_overlayItem == item)
            {
                HideLibraryOverlay();
            }
        }
    }

    private void OnLibraryItemLayoutUpdated(object? sender, object e)
    {
        UpdateLibraryOverlayPosition();
        if (sender is GridViewItem item && _overlayItem == item && _libraryOverlayImage?.Source == null)
        {
            ApplyOverlayFromItem(item);
        }
    }

    private void EnsureLibraryOverlay()
    {
        if (_libraryOverlayCard != null) return;

        _overlayScale = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
        _libraryOverlayCard = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 52, 64)),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(6),
            BorderBrush = new SolidColorBrush(Colors.White),
            Shadow = new ThemeShadow(),
            RenderTransform = _overlayScale,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            IsHitTestVisible = false
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(image);
        _libraryOverlayImage = image;

        var overlay = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Height = 60 };
        overlay.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.5, 0),
            EndPoint = new Windows.Foundation.Point(0.5, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 0 },
                new GradientStop { Color = Color.FromArgb(204, 0, 0, 0), Offset = 1 }
            }
        };
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, 2);
        grid.Children.Add(overlay);

        var title = new TextBlock
        {
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(8, 0, 8, 8),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRow(title, 1);
        grid.Children.Add(title);
        _libraryOverlayTitle = title;

        _libraryOverlayCard.Child = grid;
        _libraryOverlayCanvas.Children.Add(_libraryOverlayCard);
    }

    private void ShowLibraryOverlay(GridViewItem item)
    {
        EnsureLibraryOverlay();
        _overlayItem = item;
        ApplyOverlayFromItem(item);

        UpdateLibraryOverlayPosition();
        _libraryOverlayCard.Translation = new Vector3(0, -14, 30);
        AnimateOverlayScale(1.25);
    }

    private void HideLibraryOverlay()
    {
        _overlayItem = null;
        if (_libraryOverlayCard != null)
        {
            _libraryOverlayCard.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateLibraryOverlayPosition()
    {
        if (_overlayItem == null || _libraryOverlayCard == null) return;

        var root = _libraryOverlayCanvas as UIElement;
        if (root == null) return;

        try
        {
            var transform = _overlayItem.TransformToVisual(root);
            var rect = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, _overlayItem.ActualWidth, _overlayItem.ActualHeight));

            _libraryOverlayCard.Width = rect.Width;
            _libraryOverlayCard.Height = rect.Height;
            Canvas.SetLeft(_libraryOverlayCard, rect.X);
            Canvas.SetTop(_libraryOverlayCard, rect.Y);
            _libraryOverlayCard.Visibility = Visibility.Visible;
        }
        catch
        {
        }
    }


    private void AnimateOverlayScale(double scale)
    {
        if (_overlayScale == null) return;

        var sb = new Storyboard();
        var animX = new DoubleAnimation
        {
            To = scale,
            Duration = TimeSpan.FromMilliseconds(150),
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animX, _overlayScale);
        Storyboard.SetTargetProperty(animX, "ScaleX");
        sb.Children.Add(animX);

        var animY = new DoubleAnimation
        {
            To = scale,
            Duration = TimeSpan.FromMilliseconds(150),
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animY, _overlayScale);
        Storyboard.SetTargetProperty(animY, "ScaleY");
        sb.Children.Add(animY);

        sb.Begin();
    }

    private void ApplyOverlayFromItem(GridViewItem item)
    {
        if (_libraryOverlayImage == null || _libraryOverlayTitle == null) return;

        var image = FindDescendant<Image>(item);
        if (image?.Source != null)
        {
            _libraryOverlayImage.Source = image.Source;
        }
        else if (item.DataContext is Galgame game)
        {
            _libraryOverlayImage.Source = CreateImageSource(game.ImagePath.Value);
        }
        else
        {
            _libraryOverlayImage.Source = null;
        }

        if (item.DataContext is Galgame g)
        {
            _libraryOverlayTitle.Text = g.Name.Value;
        }
        else
        {
            var title = FindDescendant<TextBlock>(item);
            _libraryOverlayTitle.Text = title?.Text ?? string.Empty;
        }
    }

    private static ImageSource? CreateImageSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return new BitmapImage(ToImageUri(path));
        }
        catch
        {
            return null;
        }
    }

    private static Uri ToImageUri(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        var fullPath = path.Replace("\\", "/");
        return new Uri($"file:///{fullPath}", UriKind.Absolute);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T match)
            {
                return match;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private void UpdateLibraryItemSize()
    {
        if (_libraryItemsPanel == null)
        {
            _libraryItemsPanel = _libraryGridView.ItemsPanelRoot as ItemsWrapGrid;
            if (_libraryItemsPanel == null) return;
        }

        var availableWidth = XamlRoot?.Size.Width ?? _libraryGridView.ActualWidth;
        if (availableWidth <= 0) return;

        if (XamlRoot != null)
        {
            _libraryGridView.Width = XamlRoot.Size.Width;
        }

        var padding = LibraryHorizontalPadding * 2;
        availableWidth = Math.Max(0, availableWidth - padding);
        if (availableWidth <= 0) return;

        var slotMin = LibraryCardWidth + (LibraryCardMargin * 2);
        var columns = Math.Max(1, (int)Math.Floor(availableWidth / slotMin));
        var rowWidth = columns * slotMin;
        var extra = Math.Max(0, (availableWidth - rowWidth) / 2);
        var contentWidth = LibraryCardWidth;
        var contentHeight = contentWidth * LibraryAspect;

        _libraryItemsPanel.ItemWidth = slotMin;
        _libraryItemsPanel.ItemHeight = contentHeight + (LibraryCardMargin * 2);
        _libraryGridView.Padding = new Thickness(
            LibraryHorizontalPadding + extra,
            0,
            LibraryHorizontalPadding + extra,
            LibraryBottomPadding);
    }

    private static Style BuildLibraryItemContainerStyle()
    {
        var style = new Style(typeof(GridViewItem));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        return style;
    }

    private bool FocusNearestLibraryItem(FrameworkElement source)
    {
        if (_libraryGridView.ItemsPanelRoot is not Panel panel || panel.Children.Count == 0)
            return false;

        var root = Content as UIElement;
        if (root == null) return false;

        var sourceCenter = GetCenterX(root, source);
        GridViewItem? best = null;
        double bestDelta = double.MaxValue;

        foreach (var child in panel.Children)
        {
            if (child is GridViewItem item)
            {
                var delta = Math.Abs(GetCenterX(root, item) - sourceCenter);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = item;
                }
            }
        }

        if (best != null)
        {
            best.Focus(FocusState.Programmatic);
            return true;
        }

        return false;
    }

    private bool FocusNearestRecentItem(FrameworkElement source)
    {
        if (_recentListPanel.Children.Count == 0) return false;

        var root = Content as UIElement;
        if (root == null) return false;

        var sourceCenter = GetCenterX(root, source);
        Control? best = null;
        double bestDelta = double.MaxValue;

        foreach (var child in _recentListPanel.Children)
        {
            if (child is Control item)
            {
                var delta = Math.Abs(GetCenterX(root, item) - sourceCenter);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = item;
                }
            }
        }

        if (best != null)
        {
            _mainScroll.ChangeView(null, 0, null);
            best.Focus(FocusState.Programmatic);
            CenterInScrollViewer(_recentScroll, best);
            return true;
        }

        return false;
    }

    private static double GetCenterX(UIElement root, FrameworkElement element)
    {
        var transform = element.TransformToVisual(root);
        var rect = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return rect.X + (rect.Width / 2);
    }

    private void CenterInScrollViewer(ScrollViewer scroll, FrameworkElement element)
    {
        try
        {
            var transform = element.TransformToVisual(scroll);
            var rect = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
            
            double scrollCenter = scroll.HorizontalOffset + (scroll.ViewportWidth / 2);
            double itemCenter = scroll.HorizontalOffset + rect.X + (rect.Width / 2);
            double offset = itemCenter - (scroll.ViewportWidth / 2);
            
            scroll.ChangeView(offset, null, null);
        }
        catch {}
    }

    private void CenterVerticalInMainScroll(FrameworkElement element)
    {
        try
        {
            var transform = element.TransformToVisual(_mainScroll);
            var rect = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
            
            double relativeCenterY = rect.Y + (rect.Height / 2);
            double targetRelativeY = _mainScroll.ViewportHeight / 2;
            
            double delta = relativeCenterY - targetRelativeY;
            double targetOffset = _mainScroll.VerticalOffset + delta;
            
            targetOffset = Math.Max(0, Math.Min(targetOffset, _mainScroll.ScrollableHeight));
            
            if (Math.Abs(delta) > 10)
            {
                _mainScroll.ChangeView(null, targetOffset, null);
            }
        }
        catch {}
    }

    private bool IsAtTopRow(GridViewItem item)
    {
        try
        {
            var transform = item.TransformToVisual(_libraryGridView);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            return point.Y <= item.ActualHeight;
        }
        catch
        {
            return true;
        }
    }

    private void RefreshHints(InputDeviceType type)
    {
         if (type == InputDeviceType.Gamepad)
        {
            _footer.UpdateHints(new List<(string, string)>
            {
                ("A", Plugin.GetLocalized("BigScreen_Select") ?? "Select"),
                ("B", Plugin.GetLocalized("BigScreen_Back") ?? "Back")
            });
        }
        else
        {
            _footer.UpdateHints(new List<(string, string)>
            {
                ("Enter", Plugin.GetLocalized("BigScreen_Select") ?? "Select"),
                ("Esc", Plugin.GetLocalized("BigScreen_Back") ?? "Back")
            });
        }
    }

    public bool TryActivateFocusedItem()
    {
        if (_lastFocusedControl == null || ViewModel == null)
        {
            return false;
        }

        if (_lastFocusedControl is RecentGameItem recentItem)
        {
            if (recentItem.DataContext is Galgame recentGame)
            {
                ViewModel.ItemClickCommand.Execute(recentGame);
                return true;
            }
        }

        if (_lastFocusedControl is GridViewItem gridItem)
        {
            if (gridItem.DataContext is Galgame libraryGame)
            {
                ViewModel.ItemClickCommand.Execute(libraryGame);
                return true;
            }
        }

        var ancestorItem = FindAncestor<GridViewItem>(_lastFocusedControl);
        if (ancestorItem?.DataContext is Galgame ancestorGame)
        {
            ViewModel.ItemClickCommand.Execute(ancestorGame);
            return true;
        }

        return false;
    }

    private void FocusInitial()
    {
        if (_recentListPanel.Children.Count > 0)
        {
            FocusRecentItem(0);
        }
        else if (_libraryGridView.Items.Count > 0)
        {
            _libraryGridView.SelectedIndex = 0;
            (_libraryGridView.ContainerFromIndex(0) as Control)?.Focus(FocusState.Programmatic);
        }
    }

    private void UpdateFocusMap()
    {
        if (_recentListPanel.Children.Count == 0)
        {
            return;
        }

        _recentListPanel.XYFocusDown = _libraryGridView;

        foreach (var child in _recentListPanel.Children)
        {
            if (child is UIElement element)
            {
                element.XYFocusDown = _libraryGridView;
            }
        }
    }

    private static bool IsDownKey(Windows.System.VirtualKey key)
    {
        return key == Windows.System.VirtualKey.Down
               || key == Windows.System.VirtualKey.GamepadDPadDown
               || key == Windows.System.VirtualKey.GamepadLeftThumbstickDown;
    }

    private static bool IsUpKey(Windows.System.VirtualKey key)
    {
        return key == Windows.System.VirtualKey.Up
               || key == Windows.System.VirtualKey.GamepadDPadUp
               || key == Windows.System.VirtualKey.GamepadLeftThumbstickUp;
    }
}
