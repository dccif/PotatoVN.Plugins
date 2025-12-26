using PotatoVN.App.PluginBase.SaveDetection.Analyzers;
using PotatoVN.App.PluginBase.SaveDetection.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase.SaveDetection.Pipeline;

internal class AnalysisStep : IDetectionStep
{
    private const int STARTUP_GRACE_PERIOD_MS = 10000;

    public async Task ExecuteAsync(DetectionContext context)
    {
        context.Log("Starting AnalysisStep...", LogLevel.Debug);

        // 1. Startup Grace Period
        // We purposefully wait to skip the initial burst of file writes (config/system files)
        // that often occurs when a game launches.
        context.Log($"[Analysis] Waiting {STARTUP_GRACE_PERIOD_MS}ms for startup noise to settle...", LogLevel.Debug);
        await Task.Delay(STARTUP_GRACE_PERIOD_MS, context.Token);

        // Clear any candidates that accumulated during the grace period
        int dropped = 0;
        while (context.Candidates.TryDequeue(out _)) dropped++;
        if (dropped > 0)
            context.Log($"[Analysis] Dropped {dropped} candidates from startup grace period.", LogLevel.Debug);

        var analyzer = new VotingAnalyzer
        {
            Logger = (msg, level) => context.Log(msg, level)
        };
        var localCandidates = new List<PathCandidate>();

        while (!context.Token.IsCancellationRequested && !context.TargetProcess.HasExited)
        {
            // 2. Collection Window
            // Wait a bit to collect a batch of events (saves often write multiple files)
            await Task.Delay(2000, context.Token);

            // Move data to local buffer
            while (context.Candidates.TryDequeue(out var c))
                localCandidates.Add(c);

            // 3. Analysis
            if (localCandidates.Count > 0)
            {
                context.Log($"[Analysis] Processing batch of {localCandidates.Count} new candidates...", LogLevel.Debug);

                var currentWinner = analyzer.FindBestSaveDirectory(localCandidates, context.Settings, context.Game);

                if (currentWinner != null)
                {
                    // Since we have already filtered out startup noise, any strong candidate detected 
                    // during runtime is highly likely to be a genuine user save.
                    // We accept the first valid winner found post-startup.
                    context.FinalPath = currentWinner;
                    context.Log($"[Analysis] Winner confirmed: {currentWinner}", LogLevel.Info);
                    break;
                }

                // Clear the buffer after processing so we don't recycle old data
                localCandidates.Clear();
            }
        }
    }
}
