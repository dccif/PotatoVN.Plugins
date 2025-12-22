using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
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

public sealed class HomePage : Page
{
    public HomeViewModel? ViewModel { get; private set; }
    
    // UI Elements
    private StackPanel _recentListPanel;
    private GridView _libraryGridView;
    private BigScreenFooter _footer;
    private ScrollViewer _mainScroll;
    private ScrollViewer _recentScroll;

    public HomePage(List<Galgame> games)
    {
        ViewModel = new HomeViewModel(games);
        this.XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled;

        // 1. Root Grid
        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Main Content
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer
        rootGrid.Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 20)); // Deep dark bg
        Content = rootGrid;

        // 2. Header
        var header = new BigScreenHeader();
        rootGrid.Children.Add(header);
        Grid.SetRow(header, 0);

        // 3. Main Content ScrollViewer (Vertical)
        _mainScroll = new ScrollViewer 
        { 
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsTabStop = false // Important: Don't take focus
        };
        rootGrid.Children.Add(_mainScroll);
        Grid.SetRow(_mainScroll, 1);

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
        
        _recentListPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
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
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled
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

        // 4. Footer
        _footer = new BigScreenFooter();
        rootGrid.Children.Add(_footer);
        Grid.SetRow(_footer, 2);

        // Logic
        PopulateRecentGames();
        
        var binding = new Binding { Source = ViewModel, Path = new PropertyPath("LibraryGames"), Mode = BindingMode.OneWay };
        BindingOperations.SetBinding(_libraryGridView, ItemsControl.ItemsSourceProperty, binding);

        _libraryGridView.ItemClick += (s, e) => 
        {
             if (e.ClickedItem is Galgame g && ViewModel != null) ViewModel.ItemClickCommand.Execute(g);
        };
        
        // Auto-center library items vertically
        _libraryGridView.GotFocus += (s, e) =>
        {
            if (e.OriginalSource is FrameworkElement item)
            {
                CenterVerticalInMainScroll(item);
            }
        };

        // Events
        Loaded += async (s, args) =>
        {
            RefreshHints(InputManager.CurrentInput);
            InputManager.InputChanged += RefreshHints;
            
            this.PreviewKeyDown += OnPreviewKeyDown;

            await System.Threading.Tasks.Task.Delay(100);
            
            if (_recentListPanel.Children.Count > 0)
            {
                 FocusRecentItem(0);
            }
            else if (_libraryGridView.Items.Count > 0)
            {
                 _libraryGridView.SelectedIndex = 0;
                 (_libraryGridView.ContainerFromIndex(0) as Control)?.Focus(FocusState.Programmatic);
            }
        };

        Unloaded += (s, args) =>
        {
            InputManager.InputChanged -= RefreshHints;
            this.PreviewKeyDown -= OnPreviewKeyDown;
        };
    }

    private void PopulateRecentGames()
    {
        if (ViewModel == null) return;
        foreach (var game in ViewModel.RecentGames)
        {
            var item = new RecentGameItem(game);
            item.Click += (s, e) => ViewModel.ItemClickCommand.Execute(game);
            _recentListPanel.Children.Add(item);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isGamepad = e.Key >= Windows.System.VirtualKey.GamepadA && e.Key <= Windows.System.VirtualKey.GamepadRightThumbstickLeft;
        InputManager.ReportInput(isGamepad ? InputDeviceType.Gamepad : InputDeviceType.Keyboard);

        bool isUp = e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.GamepadDPadUp || e.Key == Windows.System.VirtualKey.GamepadLeftThumbstickUp;
        bool isDown = e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.GamepadDPadDown || e.Key == Windows.System.VirtualKey.GamepadLeftThumbstickDown;
        bool isLeft = e.Key == Windows.System.VirtualKey.Left || e.Key == Windows.System.VirtualKey.GamepadDPadLeft || e.Key == Windows.System.VirtualKey.GamepadLeftThumbstickLeft;
        bool isRight = e.Key == Windows.System.VirtualKey.Right || e.Key == Windows.System.VirtualKey.GamepadDPadRight || e.Key == Windows.System.VirtualKey.GamepadLeftThumbstickRight;

        if (!isUp && !isDown && !isLeft && !isRight) return;

        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;

        if (IsDescendantOf(focused, _recentListPanel))
        {
            if (isRight) { NavigateRecentHorizontal(focused, 1); e.Handled = true; }
            else if (isLeft) { NavigateRecentHorizontal(focused, -1); e.Handled = true; }
            else if (isDown) { NavigateToLibrary(); e.Handled = true; }
            else if (isUp) { e.Handled = true; } // Prevent scroll
        }
        else if (IsDescendantOf(focused, _libraryGridView))
        {
            if (isUp)
            {
                if (IsAtTopRowOfGrid(focused))
                {
                    NavigateToRecent();
                    e.Handled = true;
                }
            }
        }
        else
        {
             if (isDown || isRight) NavigateToRecent();
             e.Handled = true;
        }
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

    private void NavigateRecentHorizontal(DependencyObject focused, int delta)
    {
        int currentIndex = -1;
        for (int i = 0; i < _recentListPanel.Children.Count; i++)
        {
            if (_recentListPanel.Children[i] == focused || IsDescendantOf(focused, _recentListPanel.Children[i]))
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex >= 0)
        {
            int nextIndex = Math.Clamp(currentIndex + delta, 0, _recentListPanel.Children.Count - 1);
            if (nextIndex != currentIndex)
            {
                FocusRecentItem(nextIndex);
            }
        }
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

    private void NavigateToLibrary()
    {
        if (_libraryGridView.Items.Count > 0)
        {
            var index = _libraryGridView.SelectedIndex < 0 ? 0 : _libraryGridView.SelectedIndex;
            var container = _libraryGridView.ContainerFromIndex(index) as Control;
            
            if (container == null)
            {
                _libraryGridView.ScrollIntoView(_libraryGridView.Items[index]);
            }
            
            container = _libraryGridView.ContainerFromIndex(index) as Control;
            container?.Focus(FocusState.Programmatic);
            
            if (container == null)
            {
                 _libraryGridView.Focus(FocusState.Programmatic);
            }
        }
    }

    private void NavigateToRecent()
    {
        // Scroll to top to ensure Recent list is visible
        _mainScroll.ChangeView(null, 0, null);

        if (_recentListPanel.Children.Count > 0)
        {
             FocusRecentItem(0); 
        }
    }

    private void CenterVerticalInMainScroll(FrameworkElement element)
    {
        try
        {
            var transform = element.TransformToVisual(_mainScroll);
            var rect = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
            
            // Calculate center
            double scrollHeight = _mainScroll.ViewportHeight;
            double itemCenterY = rect.Y + (rect.Height / 2);
            
            // Current relative position is rect.Y because TransformToVisual takes current scroll into account? 
            // No, TransformToVisual gives coordinates relative to the Visual's top-left.
            // If _mainScroll is scrolled, rect.Y will be negative if above.
            
            // We need absolute position relative to content to calculate Absolute Offset.
            // Actually ChangeView takes Absolute Offset.
            // _mainScroll.VerticalOffset is the current scroll.
            // rect.Y is position relative to the Viewport (Top-Left of visible area).
            
            double relativeCenterY = rect.Y + (rect.Height / 2);
            double targetRelativeY = scrollHeight / 2;
            
            double delta = relativeCenterY - targetRelativeY;
            double targetOffset = _mainScroll.VerticalOffset + delta;
            
            // Clamp
            targetOffset = Math.Max(0, Math.Min(targetOffset, _mainScroll.ScrollableHeight));
            
            // Only scroll if significant change to avoid jitter
            if (Math.Abs(delta) > 10)
            {
                _mainScroll.ChangeView(null, targetOffset, null);
            }
        }
        catch {}
    }

    private bool IsAtTopRowOfGrid(DependencyObject focused)
    {
        if (focused is UIElement element)
        {
             try
             {
                 var transform = element.TransformToVisual(_libraryGridView);
                 var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                 return point.Y < 100; // Heuristic: < 100px from top
             }
             catch { return true; }
        }
        return true;
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
}