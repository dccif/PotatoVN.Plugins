using System;
using System.Collections.Generic;
using System.Linq;
using GalgameManager.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PotatoVN.App.PluginBase.Templates;
using Windows.System;
using Windows.UI;
using Windows.Gaming.Input; // 引入原生手柄 API

namespace PotatoVN.App.PluginBase.Views;

public class BigScreenPage : Grid
{
    private readonly GridView _gridView;
    private readonly Window _parentWindow;
    private readonly DispatcherTimer _gamepadTimer;
    private DateTime _lastInputTime = DateTime.MinValue;
    private Gamepad _activeGamepad;

    public BigScreenPage(Window parentWindow, List<Galgame> games)
    {
        _parentWindow = parentWindow;

        // 1. 设置整体背景
        this.Background = new SolidColorBrush(Color.FromArgb(255, 26, 31, 41));

        // 2. 布局定义
        // 限制 XY 导航在 GridView 内部，防止越界崩溃
        this.XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled;

        this.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        this.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 3. Header
        var headerPanel = CreateHeader();
        Grid.SetRow(headerPanel, 0);
        this.Children.Add(headerPanel);

        // 4. GridView
        _gridView = new GridView
        {
            ItemsSource = games,
            ItemTemplate = GameItemTemplate.GetTemplate(),
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            Padding = new Thickness(40, 20, 40, 40),
            IsSwipeEnabled = true,
            IsTabStop = true,
            // 启用 WinUI 内置导航作为辅助
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled
        };

        _gridView.ItemClick += GridView_ItemClick;
        _gridView.Loaded += async (s, e) =>
        {
            if (games.Count > 0)
            {
                _gridView.SelectedIndex = 0;
                await System.Threading.Tasks.Task.Delay(100);
                var container = _gridView.ContainerFromIndex(0) as Control;
                container?.Focus(FocusState.Programmatic);
            }
        };

        Grid.SetRow(_gridView, 1);
        this.Children.Add(_gridView);

        // 5. 键盘监听
        this.KeyDown += BigScreenPage_KeyDown;

        // 6. 手柄轮询计时器 (30fps 足够 UI 导航)
        _gamepadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _gamepadTimer.Tick += GamepadTimer_Tick;
        _gamepadTimer.Start();

        // 页面卸载时停止计时器
        this.Unloaded += (s, e) => _gamepadTimer.Stop();
    }

    private void GamepadTimer_Tick(object sender, object e)
    {
        if (DateTime.Now - _lastInputTime < TimeSpan.FromMilliseconds(150)) return; // 防抖

        if (_activeGamepad == null || !Gamepad.Gamepads.Contains(_activeGamepad))
        {
            _activeGamepad = Gamepad.Gamepads.FirstOrDefault();
        }

        if (_activeGamepad == null) return;

        var reading = _activeGamepad.GetCurrentReading();
        FocusNavigationDirection direction = FocusNavigationDirection.None;

        // 阈值 (Deadzone)
        double threshold = 0.5;

        // Stick & D-Pad
        if (reading.LeftThumbstickX < -threshold || (reading.Buttons & GamepadButtons.DPadLeft) != 0)
            direction = FocusNavigationDirection.Left;
        else if (reading.LeftThumbstickX > threshold || (reading.Buttons & GamepadButtons.DPadRight) != 0)
            direction = FocusNavigationDirection.Right;
        else if (reading.LeftThumbstickY > threshold || (reading.Buttons & GamepadButtons.DPadUp) != 0) // Y is up positive
            direction = FocusNavigationDirection.Up;
        else if (reading.LeftThumbstickY < -threshold || (reading.Buttons & GamepadButtons.DPadDown) != 0)
            direction = FocusNavigationDirection.Down;

        if (direction != FocusNavigationDirection.None)
        {
            TryNavigate(direction);
            _lastInputTime = DateTime.Now;
        }
        else if ((reading.Buttons & GamepadButtons.A) != 0) // A 键确认
        {
            // 模拟点击当前焦点元素
            var focused = FocusManager.GetFocusedElement(this.XamlRoot) as ButtonBase; // Button or similar
            // GridViewItem 不是 ButtonBase，我们需要处理 GridViewItem 的点击
            // 或者直接触发 GridView 的当前选中项
            if (focused == null)
            {
                // 如果焦点在 GridViewItem 上
                if (FocusManager.GetFocusedElement(this.XamlRoot) is ListViewItem item)
                {
                    // 这种方式比较 hack，更好的方式是让 GridViewItem 响应 AutomationPeer
                    // 但这里我们简单点：既然它是选中项，我们直接调用 ItemClick 逻辑
                    // 也就是 _gridView.SelectedItem
                    if (_gridView.SelectedItem is Galgame game)
                    {
                        System.Diagnostics.Debug.WriteLine($"Gamepad Launch: {game.Name.Value}");
                        // TODO: Launch
                    }
                }
            }
            else
            {
                // 如果是按钮（如退出按钮），通过 UI Automation 点击
                // 或者简单调用 focused.Command?
                // 模拟回车键最简单
            }
            _lastInputTime = DateTime.Now.AddMilliseconds(200); // 确认键防抖长一点
        }
        else if ((reading.Buttons & GamepadButtons.B) != 0) // B 键退出
        {
            _parentWindow.Close();
            _lastInputTime = DateTime.Now.AddMilliseconds(500);
        }
    }

    private void TryNavigate(FocusNavigationDirection direction)
    {
        try
        {
            // 尝试移动焦点
            var options = new FindNextElementOptions { SearchRoot = this.XamlRoot.Content };
            var next = FocusManager.FindNextElement(direction, options);
            if (next != null)
            {
                // 移动成功
                FocusManager.TryMoveFocus(direction, options);
            }
            else
            {
                // 移动失败（越界），什么都不做，防止崩溃
                // 或者可以在这里实现循环滚动
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation Error: {ex.Message}");
        }
    }

    private StackPanel CreateHeader()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Padding = new Thickness(40, 30, 40, 10),
            Spacing = 20
        };

        var exitBtn = new Button
        {
            Content = "EXIT (B/Esc)",
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(Colors.White)
        };
        exitBtn.Click += (s, e) => _parentWindow.Close();

        var title = new TextBlock
        {
            Text = "LIBRARY",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.Light,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(exitBtn);
        panel.Children.Add(title);
        return panel;
    }

    private void GridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Galgame game)
        {
            // 播放点击动画或效果
            // 这里可以调用启动逻辑，目前仅打印
            System.Diagnostics.Debug.WriteLine($"Launch Game: {game.Name.Value}");

            // TODO: 调用 HostApi 启动游戏
            // var api = ...; 
            // api.LaunchGame(game);
        }
    }

    private void BigScreenPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // 仅处理键盘的 ESC 退出，手柄的 B 键由 Timer 处理
        if (e.Key == VirtualKey.Escape)
        {
            _parentWindow.Close();
            e.Handled = true;
        }
    }
}
