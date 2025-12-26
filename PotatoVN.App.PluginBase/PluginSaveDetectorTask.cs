using CommunityToolkit.Mvvm.Messaging;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using PotatoVN.App.PluginBase.SaveDetection;
using PotatoVN.App.PluginBase.SaveDetection.Models;
using PotatoVN.App.PluginBase.SaveDetection.Pipeline;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase;

public class PluginSaveDetectorTask : BgTaskBase
{
    private readonly Galgame _game;
    private readonly IMessenger? _messenger;
    private readonly SaveDetectorOptions _options;

    private Process? _gameProcess;

    public override string Title => Plugin.GetLocalized("GameSaveDetectorTask_Title") ?? "Save Detection";
    public override bool CanCancel => true;
    public override bool ProgressOnTrayIcon => true;

    // For serialization (BgTaskBase requires parameterless constructor)
    public PluginSaveDetectorTask()
    {
        _game = new Galgame();
        _options = new SaveDetectorOptions();
    }

    public PluginSaveDetectorTask(Galgame game, IMessenger? messenger = null, bool useAdminMode = false)
    {
        _game = game;
        _messenger = messenger;
        _options = new SaveDetectorOptions { AllowEtw = useAdminMode };
        CancellationTokenSource = new CancellationTokenSource();
    }

    protected override Task RecoverFromJsonInternal()
    {
        return Task.CompletedTask;
    }

    protected override async Task RunInternal()
    {
        try
        {
            if (_game == null) return;

            // Apply timeout
            CancellationTokenSource?.CancelAfter(TimeSpan.FromSeconds(_options.MaxDetectionTimeSeconds));

            // 1. Find process (Orchestration Step 1)
            ChangeProgress(0, 1, Plugin.GetLocalized("GameSaveDetector_Initializing") ?? "Initializing...");
            _gameProcess = await WaitForGameProcessAsync();

            if (_gameProcess == null || _gameProcess.HasExited)
            {
                ChangeProgress(-1, 100, Plugin.GetLocalized("GameSaveDetector_ProcessNotFound") ?? "Game process not found");
                return;
            }

            // 2. Initialize Context (Orchestration Step 2)
            ISaveDetectorLogger taskLogger = new BgTaskLogger(this);

            // Note: We use CancellationToken from BgTaskBase
            var context = new DetectionContext(_gameProcess, CancellationToken!.Value, taskLogger, _options)
            {
                Game = _game
            };

            ChangeProgress(0, 1, Plugin.GetLocalized("GameSaveDetector_Monitoring") ?? "Monitoring...", false);

            // 3. Run Pipeline (Orchestration Step 3)
            // We define the pipeline explicitly here as per requirement
            var pipeline = new List<IDetectionStep>
            {
                new DiscoveryStep(),
                new AnalysisStep()
            };

            try
            {
                foreach (var step in pipeline)
                {
                    if (CancellationToken.Value.IsCancellationRequested) break;
                    await step.ExecuteAsync(context);
                }
            }
            finally
            {
                // Ensure provider is stopped when task finishes or is cancelled
                context.ActiveProvider?.Stop();
                if (context.ActiveProvider is IDisposable d) d.Dispose();
            }

            // 4. Handle Result (Orchestration Step 4)
            if (context.FinalPath != null)
            {
                _game.DetectedSavePath = GamePortablePath.Create(context.FinalPath, _game.LocalPath);

                string msgTemplate = Plugin.GetLocalized("GameSaveDetector_Success") ?? "Detected: {0}";
                string msg = string.Format(msgTemplate, context.FinalPath);

                ChangeProgress(1, 1, msg, true);
            }
            else
            {
                if (CancellationToken.Value.IsCancellationRequested)
                {
                    // Check if it was a timeout or manual cancellation
                    // Note: This is a bit of a heuristic since we don't have a separate timeout token,
                    // but we can check if the time elapsed is close to the limit.
                    // Or we just use a generic cancelled message, but user asked for "stopped" on timeout.
                    ChangeProgress(-1, 1, Plugin.GetLocalized("GameSaveDetector_Timeout") ?? "Detection timeout");
                }
                else
                {
                    ChangeProgress(-1, 1, Plugin.GetLocalized("GameSaveDetector_NotFound") ?? "No save detected");
                }
            }
        }
        catch (OperationCanceledException)
        {
            ChangeProgress(-1, 1, Plugin.GetLocalized("GameSaveDetector_Timeout") ?? "Detection timeout");
        }
        catch (Exception ex)
        {
            ChangeProgress(-1, 1, $"{Plugin.GetLocalized("GameSaveDetector_Failed")}: {ex.Message}");
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private async Task<Process?> WaitForGameProcessAsync()
    {
        if (string.IsNullOrEmpty(_game?.ExePath)) return null;

        string exeName = Path.GetFileNameWithoutExtension(_game.ExePath);

        // Wait using configurable time
        for (int i = 0; i < _options.ProcessWaitTimeSeconds; i++)
        {
            if (CancellationToken != null && CancellationToken.Value.IsCancellationRequested) return null;

            try
            {
                var processes = Process.GetProcessesByName(exeName);
                if (processes.Length > 0) return processes[0];

                // Fallback: Use the currently active window
                var hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(hwnd, out var pid);
                    if (pid != 0)
                    {
                        var p = Process.GetProcessById((int)pid);
                        if (p != null && p.Id != Environment.ProcessId && !p.HasExited)
                        {
                            return p;
                        }
                    }
                }
            }
            catch { }

            await Task.Delay(1000);
        }
        return null;
    }

    /// <summary>
    /// Internal Logger Bridge: Maps low-level logs to UI Progress
    /// </summary>
    private class BgTaskLogger : ISaveDetectorLogger
    {
        private readonly PluginSaveDetectorTask _parent;

        public BgTaskLogger(PluginSaveDetectorTask parent) => _parent = parent;

        public void Log(string message, LogLevel level)
        {
            // Only show Info/Warning/Error in UI to avoid spamming
            if (level >= LogLevel.Info)
            {
                // Keep the current percentage, just update message
                _parent.ChangeProgress(_parent.CurrentProgress.Current, 1, message, false);
            }

            // Always output to debug console for development
            Debug.WriteLine($"[PluginSaveDetectorTask] [{level}] {message}");
        }
    }
}
