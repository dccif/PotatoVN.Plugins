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
        scaffold.Body = _mainScroll;

        var contentStack = new StackPanel { Padding = new Thickness(0, 20, 0, 40) };
        _mainScroll.Content = contentStack;

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
            Padding = new Thickness(60, 0, 60, 40),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectionMode = ListViewSelectionMode.None, // Prevent persistent selection state (ghosting)
            IsItemClickEnabled = true,
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled,
            XYFocusUpNavigationStrategy = XYFocusNavigationStrategy.Projection
        };
        contentStack.Children.Add(_libraryGridView);

        string libraryTemplate = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
    <Grid Width='160' Height='240' Margin='6' Background='#2d3440' CornerRadius='6'>
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
                item.KeyDown -= OnLibraryItemKeyDown;
                item.KeyDown += OnLibraryItemKeyDown;
                if (_recentListPanel.Children.Count > 0)
                {
                    item.XYFocusUp = _recentListPanel;
                }
            }
        };
        
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
        _libraryGridView.XYFocusUp = _recentListPanel;

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
