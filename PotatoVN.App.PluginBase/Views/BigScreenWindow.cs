using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using GalgameManager.Models;
using System.Runtime.InteropServices;

namespace PotatoVN.App.PluginBase.Views;

public class BigScreenWindow : Window
{
    // PInvoke definitions
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_MAXIMIZE = 0x01000000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOP = new IntPtr(0);

    public BigScreenWindow(List<Galgame> games, Galgame? initialGame = null)
    {
        // 1. 设置内容
        Content = new BigScreenPage(this, games, initialGame);

        // 2. 设置全屏逻辑 (手动移除边框并定位)
        PositionWindowOnCurrentMonitor();

        // 3. 监听激活事件，确保窗口在获得焦点时覆盖任务栏 (修复OBS等录屏软件导致的Z-Order问题)
        Activated += (s, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                SetWindowPos(hWnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                if (Content is BigScreenPage page)
                {
                    page.RequestFocus();
                }
            }
        };
    }

    private void PositionWindowOnCurrentMonitor()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        try
        {
            // 1. Ensure we are in a basic state (Overlapped)
            if (appWindow.Presenter.Kind != AppWindowPresenterKind.Overlapped)
            {
                appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            }

            // 2. Identify the target monitor
            if (GetCursorPos(out POINT lpPoint))
            {
                var displayArea = DisplayArea.GetFromPoint(
                    new Windows.Graphics.PointInt32(lpPoint.X, lpPoint.Y),
                    DisplayAreaFallback.Primary);

                if (displayArea != null)
                {
                    // 3. Remove standard Window styles (TitleBar, Borders) via P/Invoke
                    // This is more reliable than AppWindow for true borderless behavior
                    int style = GetWindowLong(hWnd, GWL_STYLE);
                    style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
                    style |= WS_POPUP | WS_MAXIMIZE; // Add WS_POPUP and WS_MAXIMIZE to ensure taskbar coverage
                    SetWindowLong(hWnd, GWL_STYLE, style);

                    // 4. Force position and size to cover the entire monitor (OuterBounds)
                    // SWP_FRAMECHANGED tells the OS to recalculate the client area (removing the chrome)
                    SetWindowPos(hWnd, HWND_TOP,
                        displayArea.OuterBounds.X,
                        displayArea.OuterBounds.Y,
                        displayArea.OuterBounds.Width,
                        displayArea.OuterBounds.Height,
                        SWP_FRAMECHANGED | SWP_SHOWWINDOW);

                    // Optional: Ensure AppWindow thinks it's borderless too, though P/Invoke overrides it usually
                    if (appWindow.Presenter is OverlappedPresenter op)
                    {
                        op.SetBorderAndTitleBar(false, false);
                        op.IsResizable = false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FullScreen Error: {ex}");
            // Fallback
            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
    }
}
