using CommunityToolkit.Mvvm.Messaging;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using PotatoVN.App.PluginBase.SaveDetection;
using PotatoVN.App.PluginBase.SaveDetection.Models;
using PotatoVN.App.PluginBase.SaveDetection.Pipeline;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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

    public PluginSaveDetectorTask(Galgame game, IMessenger? messenger = null, bool useAdminMode = false, int stabilityCycles = 3)
    {
        _game = game;
        _messenger = messenger;
        _options = new SaveDetectorOptions { AllowEtw = useAdminMode, StabilityCycles = stabilityCycles };
        CancellationTokenSource = new CancellationTokenSource();
    }

    protected override Task RecoverFromJsonInternal()
    {
        return Task.CompletedTask;
    }

    protected override async Task RunInternal()
    {
        DetectionContext? context = null;
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
                ChangeProgress(-1, 100,
                    Plugin.GetLocalized("GameSaveDetector_ProcessNotFound") ?? "Game process not found");
                return;
            }

            // 2. Initialize Context (Orchestration Step 2)
            ISaveDetectorLogger taskLogger = new BgTaskLogger(this);

            // Note: We use CancellationToken from BgTaskBase
            context = new DetectionContext(_gameProcess, CancellationToken!.Value, taskLogger, _options)
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

                var msgTemplate = Plugin.GetLocalized("GameSaveDetector_Success") ?? "Detected: {0}";
                var msg = string.Format(msgTemplate, context.FinalPath);

                ChangeProgress(1, 1, msg, true);

                ShowNotification(Title, msg);

                // 探测成功，也写入日志以便验证结果是否正确
                WriteDetectionLog(context, true, context.FinalPath);
            }
            else
            {
                if (CancellationToken.Value.IsCancellationRequested)
                    ChangeProgress(-1, 1, Plugin.GetLocalized("GameSaveDetector_Timeout") ?? "Detection timeout");
                else
                    ChangeProgress(-1, 1, Plugin.GetLocalized("GameSaveDetector_NotFound") ?? "No save detected");

                // 探测失败，将日志写入文件
                WriteDetectionLog(context, false);
            }
        }
        catch (OperationCanceledException)
        {
            ChangeProgress(-1, 1, Plugin.GetLocalized("GameSaveDetector_Timeout") ?? "Detection timeout");
            WriteDetectionLog(context, false, reason: "OperationCancelled/Timeout");
        }
        catch (Exception ex)
        {
            ChangeProgress(-1, 1, $"{Plugin.GetLocalized("GameSaveDetector_Failed")}: {ex.Message}");
            WriteDetectionLog(context, false, reason: $"Exception: {ex.Message}");
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private async Task<Process?> WaitForGameProcessAsync()
    {
        if (string.IsNullOrEmpty(_game?.ExePath)) return null;

        var exeName = Path.GetFileNameWithoutExtension(_game.ExePath);

        // Wait using configurable time
        for (var i = 0; i < _options.ProcessWaitTimeSeconds; i++)
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
                        if (p != null && p.Id != Environment.ProcessId && !p.HasExited) return p;
                    }
                }
            }
            catch
            {
            }

            await Task.Delay(1000);
        }

        return null;
    }

    private void ShowNotification(string title, string message)
    {
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginSaveDetectorTask] Failed to show notification: {ex.Message}");
        }
    }

    /// <summary>
    /// 日志文件数量上限
    /// </summary>
    private const int MaxLogFiles = 5;

    /// <summary>
    /// 单个日志文件大小上限（512KB）
    /// </summary>
    private const long MaxLogFileSize = 512 * 1024;

    /// <summary>
    /// 将探测日志写入文件（无论成功或失败均调用），并自动轮转旧日志
    /// </summary>
    /// <param name="context">探测上下文</param>
    /// <param name="success">探测是否成功</param>
    /// <param name="detectedPath">成功时探测到的路径</param>
    /// <param name="reason">失败时的附加原因</param>
    private void WriteDetectionLog(DetectionContext? context, bool success, string? detectedPath = null, string? reason = null)
    {
        try
        {
            if (context == null) return;
            var logContent = context.GetBufferedLog();
            if (string.IsNullOrWhiteSpace(logContent)) return;

            var logDir = Path.Combine(XamlResourceLocatorFactory.packagePath, "detection_logs");
            Directory.CreateDirectory(logDir);

            // 构建日志头部信息
            var header = new StringBuilder();
            header.AppendLine("========================================");
            header.AppendLine($"  Detection Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            header.AppendLine($"  Game: {_game?.Name?.Value ?? _game?.ExePath ?? "Unknown"}");
            header.AppendLine($"  Result: {(success ? "SUCCESS" : "FAILED")}");
            if (success && detectedPath != null)
                header.AppendLine($"  Detected Path: {detectedPath}");
            if (!success && reason != null)
                header.AppendLine($"  Reason: {reason}");
            header.AppendLine("========================================");
            header.AppendLine();

            var fullContent = header.ToString() + logContent;

            // 如果日志内容超过单文件大小，截断尾部保留最近的内容
            if (fullContent.Length > MaxLogFileSize)
            {
                var truncateNote = $"[...truncated, original size: {fullContent.Length} bytes...]\n";
                fullContent = truncateNote + fullContent.Substring(fullContent.Length - (int)MaxLogFileSize + truncateNote.Length);
            }

            // 写入带时间戳的日志文件
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var resultTag = success ? "ok" : "fail";
            var logFileName = $"detection_{timestamp}_{resultTag}.log";
            var logPath = Path.Combine(logDir, logFileName);
            File.WriteAllText(logPath, fullContent);

            // 清理旧日志文件，只保留最新的 MaxLogFiles 个
            CleanupOldLogs(logDir);

            Debug.WriteLine($"[PluginSaveDetectorTask] Detection log saved to: {logPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginSaveDetectorTask] Failed to write detection log: {ex.Message}");
        }
        finally
        {
            context?.ClearLog();
        }
    }

    /// <summary>
    /// 清理旧的日志文件，只保留最新的 MaxLogFiles 个
    /// </summary>
    private static void CleanupOldLogs(string logDir)
    {
        try
        {
            var logFiles = Directory.GetFiles(logDir, "detection_*.log")
                .OrderByDescending(f => File.GetCreationTime(f))
                .ToArray();

            for (var i = MaxLogFiles; i < logFiles.Length; i++)
            {
                try { File.Delete(logFiles[i]); }
                catch { /* 忽略单个文件删除失败 */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginSaveDetectorTask] Failed to cleanup old logs: {ex.Message}");
        }
    }

    /// <summary>
    /// Internal Logger Bridge: Maps low-level logs to UI Progress
    /// </summary>
    private class BgTaskLogger : ISaveDetectorLogger
    {
        private readonly PluginSaveDetectorTask _parent;

        public BgTaskLogger(PluginSaveDetectorTask parent)
        {
            _parent = parent;
        }

        public void Log(string message, LogLevel level)
        {
            // Only show Info/Warning/Error in UI to avoid spamming
            if (level >= LogLevel.Info && _parent.CurrentProgress != null)
                // Keep the current percentage, just update message
                _parent.ChangeProgress(_parent.CurrentProgress.Current, 1, message, false);

            // Always output to debug console for development
            Debug.WriteLine($"[PluginSaveDetectorTask] [{level}] {message}");
        }
    }
}