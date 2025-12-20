using System;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using PotatoVN.App.PluginBase.SaveDetection.Models;

namespace PotatoVN.App.PluginBase.SaveDetection.Providers;

internal class EtwProvider : ISaveCandidateProvider
{
    private TraceEventSession? _session;

    public async Task StartAsync(DetectionContext context, Func<string, bool> pathFilter)
    {
        _ = Task.Run(() => {
            try {
                _session = new TraceEventSession("NT Kernel Logger");
                _session.Stop(true);
                _session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIO | KernelTraceEventParser.Keywords.FileIOInit);

                // Handler for FileIOCreate
                Action<Microsoft.Diagnostics.Tracing.Parsers.Kernel.FileIOCreateTraceData> createHandler = data => {
                    if (data.ProcessID == context.TargetProcess.Id)
                    {
                        if (!string.IsNullOrEmpty(data.FileName) && pathFilter(data.FileName)) 
                        {
                            context.Candidates.Enqueue(new PathCandidate(data.FileName, ProviderSource.ETW, DateTime.Now));
                        }
                    }
                };

                // Handler for FileIOWrite
                Action<Microsoft.Diagnostics.Tracing.Parsers.Kernel.FileIOReadWriteTraceData> writeHandler = data => {
                    if (data.ProcessID == context.TargetProcess.Id)
                    {
                        if (!string.IsNullOrEmpty(data.FileName) && pathFilter(data.FileName)) 
                        {
                            context.Candidates.Enqueue(new PathCandidate(data.FileName, ProviderSource.ETW, DateTime.Now));
                        }
                    }
                };

                _session.Source.Kernel.FileIOWrite += writeHandler;
                _session.Source.Kernel.FileIOCreate += createHandler;
                _session.Source.Process();
            } catch (Exception ex) { context.Log($"ETW Critical Error: {ex.Message}", LogLevel.Error); }
        }, context.Token);
        await Task.CompletedTask;
    }

    public void Stop() => _session?.Dispose();
}