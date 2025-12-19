using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using GalgameManager.Models;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using PotatoVN.App.PluginBase.Enums;

namespace PotatoVN.App.PluginBase.Services
{
    public class EtwSaveDetector
    {
        private static readonly ConcurrentBag<string> _detectedCandidates = new();

        public static async Task<string?> DetectSavePathAsync(Process process, Galgame? game, CancellationToken token)
        {
            if (game == null) return null;

            _detectedCandidates.Clear();
            var analyzer = new SavePathAnalyzer(game);
            
            Debug.WriteLine($"[EtwSaveDetector] Attempting to start ETW Kernel Session for Process {process.Id}...");

            try
            {
                var tcs = new TaskCompletionSource<string?>();
                
                // 尝试直接创建内核会话。如果权限不足，这一步会抛出 UnauthorizedAccessException
                using (var session = new TraceEventSession("NT Kernel Logger")) 
                {
                    session.Stop(true); // 尝试停止可能存在的旧内核会话
                    
                    session.EnableKernelProvider(
                        KernelTraceEventParser.Keywords.FileIO | 
                        KernelTraceEventParser.Keywords.FileIOInit); 

                    session.Source.Kernel.FileIOWrite += data => 
                    {
                        if (data.ProcessID == process.Id) HandleFileActivity(data.FileName, analyzer);
                    };
                    session.Source.Kernel.FileIOCreate += data => 
                    {
                        if (data.ProcessID == process.Id) HandleFileActivity(data.FileName, analyzer);
                    };

                    var processingTask = Task.Run(() => session.Source.Process());

                    while (!token.IsCancellationRequested)
                    {
                        if (process.HasExited) break;

                        if (!_detectedCandidates.IsEmpty)
                        {
                            var candidates = _detectedCandidates.ToList();
                            var best = analyzer.FindBestSaveDirectory(candidates);
                            if (best != null && candidates.Count >= 3) 
                            {
                                tcs.TrySetResult(best);
                                break;
                            }
                        }
                        await Task.Delay(1000, token);
                    }
                    session.Stop();
                }

                return await tcs.Task.ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null);
            }
            catch (UnauthorizedAccessException ex)
            {
                // 权限不足：降级到普通模式
                Debug.WriteLine($"[EtwSaveDetector] Access Denied: {ex.Message}. Falling back to Normal Polling Mode.");
                return await SaveFileDetector.DetectSavePathAsync(process, game, token);
            }
            catch (Exception ex)
            {
                // 其他 ETW 错误（如会话已被占用）：降级
                Debug.WriteLine($"[EtwSaveDetector] ETW Error: {ex.Message}. Falling back to Normal Polling Mode.");
                return await SaveFileDetector.DetectSavePathAsync(process, game, token);
            }
        }

        private static void HandleFileActivity(string path, SavePathAnalyzer analyzer)
        {
            if (string.IsNullOrEmpty(path) || !path.Contains(':')) return;
            if (analyzer.IsPotentialSaveFile(path))
            {
                _detectedCandidates.Add(path);
                Debug.WriteLine($"[EtwSaveDetector] Captured: {path}");
            }
        }

        public static bool IsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }
    }
}
