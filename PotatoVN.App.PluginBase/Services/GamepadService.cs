using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PotatoVN.App.PluginBase.Models;

namespace PotatoVN.App.PluginBase.Services;

public class GamepadService
{
    private static GamepadService? _instance;
    public static GamepadService Instance => _instance ??= new GamepadService();

    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    public bool IsRunning { get; private set; }

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

    private const int ERROR_SUCCESS = 0;
    
    // XInput Button Constants
    private const int XINPUT_GAMEPAD_DPAD_UP        = 0x0001;
    private const int XINPUT_GAMEPAD_DPAD_DOWN      = 0x0002;
    private const int XINPUT_GAMEPAD_DPAD_LEFT      = 0x0004;
    private const int XINPUT_GAMEPAD_DPAD_RIGHT     = 0x0008;
    private const int XINPUT_GAMEPAD_START          = 0x0010;
    private const int XINPUT_GAMEPAD_BACK           = 0x0020;
    private const int XINPUT_GAMEPAD_LEFT_THUMB     = 0x0040;
    private const int XINPUT_GAMEPAD_RIGHT_THUMB    = 0x0080;
    private const int XINPUT_GAMEPAD_LEFT_SHOULDER  = 0x0100;
    private const int XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
    private const int XINPUT_GAMEPAD_A              = 0x1000;
    private const int XINPUT_GAMEPAD_B              = 0x2000;
    private const int XINPUT_GAMEPAD_X              = 0x4000;
    private const int XINPUT_GAMEPAD_Y              = 0x8000;
    private const int XINPUT_GAMEPAD_GUIDE          = 0x0400;

    private ushort _lastButtons = 0;

    private GamepadService() { }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        _pollingTask = Task.Factory.StartNew(PollingLoop, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        
        _cts?.Cancel();
        IsRunning = false;
        // We don't wait for the task to finish to avoid blocking UI, 
        // the token cancellation is enough to stop the loop eventually.
    }

    private async Task PollingLoop()
    {
        var token = _cts!.Token;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20)); // High responsiveness
        
        while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
        {
            ProcessInput();
        }
    }

    private void ProcessInput()
    {
        try
        {
            XINPUT_STATE state;
            if (XInputGetStateEx(0, out state) == ERROR_SUCCESS)
            {
                var currentButtons = state.Gamepad.wButtons;
                var changedButtons = (ushort)(currentButtons ^ _lastButtons);
                var pressedButtons = (ushort)(changedButtons & currentButtons);

                if (pressedButtons != 0)
                {
                    if ((pressedButtons & XINPUT_GAMEPAD_A) != 0) Publish(GamepadButton.A);
                    if ((pressedButtons & XINPUT_GAMEPAD_B) != 0) Publish(GamepadButton.B);
                    if ((pressedButtons & XINPUT_GAMEPAD_X) != 0) Publish(GamepadButton.X);
                    if ((pressedButtons & XINPUT_GAMEPAD_Y) != 0) Publish(GamepadButton.Y);
                    if ((pressedButtons & XINPUT_GAMEPAD_DPAD_UP) != 0) Publish(GamepadButton.Up);
                    if ((pressedButtons & XINPUT_GAMEPAD_DPAD_DOWN) != 0) Publish(GamepadButton.Down);
                    if ((pressedButtons & XINPUT_GAMEPAD_DPAD_LEFT) != 0) Publish(GamepadButton.Left);
                    if ((pressedButtons & XINPUT_GAMEPAD_DPAD_RIGHT) != 0) Publish(GamepadButton.Right);
                    if ((pressedButtons & XINPUT_GAMEPAD_START) != 0) Publish(GamepadButton.Start);
                    if ((pressedButtons & XINPUT_GAMEPAD_BACK) != 0) Publish(GamepadButton.Select);
                    if ((pressedButtons & XINPUT_GAMEPAD_GUIDE) != 0) Publish(GamepadButton.Guide);
                }

                _lastButtons = currentButtons;
            }
        }
        catch { }
    }
    
    private void Publish(GamepadButton btn)
    {
        SimpleEventBus.Instance.Publish(new GamepadInputMessage(btn));
    }
}
