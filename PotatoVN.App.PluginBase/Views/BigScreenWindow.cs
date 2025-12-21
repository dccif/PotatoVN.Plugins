using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using GalgameManager.Models;
using System.Runtime.InteropServices;

namespace PotatoVN.App.PluginBase.Views;

public class BigScreenWindow : Window
{
    // PInvoke definitions for cursor position
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    public BigScreenWindow(List<Galgame> games)
    {
        // 1. 设置全屏逻辑 (定位到当前鼠标所在屏幕)
        PositionWindowOnCurrentMonitor();

        // 2. 设置内容为纯 C# 构建的 Page
        // 传入 'this' 以便 Page 可以控制窗口关闭
        this.Content = new BigScreenPage(this, games);

        // 3. 隐藏标题栏 (可选，但在全屏模式下通常由 SetPresenter 处理)
        this.ExtendsContentIntoTitleBar = true;
    }

    private void PositionWindowOnCurrentMonitor()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        try
        {
            if (GetCursorPos(out POINT lpPoint))
            {
                var displayArea = DisplayArea.GetFromPoint(
                    new Windows.Graphics.PointInt32(lpPoint.X, lpPoint.Y),
                    DisplayAreaFallback.Primary);

                if (displayArea != null)
                {
                    // 移动到目标屏幕的原点
                    appWindow.Move(new Windows.Graphics.PointInt32(displayArea.OuterBounds.X, displayArea.OuterBounds.Y));
                }
            }
        }
        catch { }

        appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }
}
