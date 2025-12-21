using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace PotatoVN.App.PluginBase.Services;

public class GamepadService : IDisposable
{
    private static GamepadService? _instance;
    public static GamepadService Instance => _instance ??= new GamepadService();

    public event Action? GuideButtonPressed;

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pollingTask;

    // XInput structures
    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "#100")]
    private static extern int XInputGetStateEx(int dwUserIndex, out XINPUT_STATE pState);

    private const int XINPUT_GAMEPAD_GUIDE = 0x0400;
    private const int ERROR_SUCCESS = 0;

    private GamepadService()
    {
        _pollingTask = Task.Factory.StartNew(PollingLoop, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private async Task PollingLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        bool wasPressed = false;

        while (await timer.WaitForNextTickAsync(_cts.Token))
        {
            bool isPressed = CheckGuideButton();

            // Simple edge detection (Trigger on Press)
            if (isPressed && !wasPressed)
            {
                // Invoke on background thread, subscribers must marshal to UI thread if needed
                GuideButtonPressed?.Invoke();
            }

            wasPressed = isPressed;
        }
    }

    private bool CheckGuideButton()
    {
        try
        {
            for (int i = 0; i < 4; i++)
            {
                XINPUT_STATE state;
                if (XInputGetStateEx(i, out state) == ERROR_SUCCESS)
                {
                    if ((state.Gamepad.wButtons & XINPUT_GAMEPAD_GUIDE) != 0)
                    {
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
