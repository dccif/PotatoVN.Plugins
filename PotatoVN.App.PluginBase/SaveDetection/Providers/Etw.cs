using System;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using PotatoVN.App.PluginBase.SaveDetection.Models;

namespace PotatoVN.App.PluginBase.SaveDetection.Providers;

internal class EtwProvider : ISaveCandidateProvider
{
    private TraceEventSession? _session;
    private const string SESSION_NAME = "PotatoVN-SaveDetector-Session";

    public async Task StartAsync(DetectionContext context, Func<string, IoOperation, bool> pathFilter)
    {
        _ = Task.Run(() =>
        {
            try
            {
                // Use a unique name for the session if possible, though Kernel sessions are often restricted
                // In many environments, "NT Kernel Logger" is the only one allowed for certain kernel events.
                _session = new TraceEventSession(KernelTraceEventParser.KernelSessionName);
                _session.Stop(true); // Clean up any previous abandoned session

                _session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIO | KernelTraceEventParser.Keywords.FileIOInit);

                // Handler for FileIOCreate (Open/Create)
                _session.Source.Kernel.FileIOCreate += data =>
                {
                    if (data.ProcessID == context.TargetProcess.Id && !string.IsNullOrEmpty(data.FileName))
                    {
                        if (pathFilter(data.FileName, IoOperation.Create))
                        {
                            context.Candidates.Enqueue(new PathCandidate(data.FileName, ProviderSource.ETW, DateTime.Now, IoOperation.Create));
                            context.Log($"[ETW] Candidate via Create: {data.FileName}", LogLevel.Debug);
                        }
                    }
                };

                // Handler for FileIOWrite
                _session.Source.Kernel.FileIOWrite += data =>
                {
                    if (data.ProcessID == context.TargetProcess.Id && !string.IsNullOrEmpty(data.FileName))
                    {
                        if (pathFilter(data.FileName, IoOperation.Write))
                        {
                            context.Candidates.Enqueue(new PathCandidate(data.FileName, ProviderSource.ETW, DateTime.Now, IoOperation.Write));
                            context.Log($"[ETW] Candidate via Write: {data.FileName}", LogLevel.Debug);
                        }
                    }
                };

                // Handler for FileIORename
                _session.Source.Kernel.FileIORename += data =>
                {
                    if (data.ProcessID == context.TargetProcess.Id && !string.IsNullOrEmpty(data.FileName))
                    {
                        if (pathFilter(data.FileName, IoOperation.Rename))
                        {
                            context.Candidates.Enqueue(new PathCandidate(data.FileName, ProviderSource.ETW, DateTime.Now, IoOperation.Rename));
                            context.Log($"[ETW] Candidate via Rename: {data.FileName}", LogLevel.Debug);
                        }
                    }
                };

                context.Log("[ETW] Session started and monitoring File IO.", LogLevel.Debug);
                _session.Source.Process();
            }
            catch (Exception ex)
            {
                context.Log($"ETW Critical Error: {ex.Message}", LogLevel.Error);
            }
        }, context.Token);
        await Task.CompletedTask;
    }

    public void Stop()
    {
        try
        {
            _session?.Stop();
            _session?.Dispose();
            _session = null;
        }
        catch { }
    }
}