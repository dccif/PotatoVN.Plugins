using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PotatoVN.App.PluginBase.SaveDetection.Analyzers;
using PotatoVN.App.PluginBase.SaveDetection.Models;

namespace PotatoVN.App.PluginBase.SaveDetection.Pipeline;

internal class AnalysisStep : IDetectionStep
{
    private const int REQUIRED_STABILITY = 3;

    public async Task ExecuteAsync(DetectionContext context)
    {
        context.Log("Starting AnalysisStep...", LogLevel.Debug);
        var analyzer = new VotingAnalyzer
        {
            Logger = (msg, level) => context.Log(msg, level)
        };
        var localCandidates = new List<PathCandidate>();
        
        string? lastWinner = null;
        int stabilityCounter = 0;
        
        // Dynamic delay logic: Start slow (15s) to allow game startup to settle.
        // Once we find a stable candidate, we accelerate to confirm it quickly.
        int currentDelay = 15000; 

        while (!context.Token.IsCancellationRequested && !context.TargetProcess.HasExited)
        {
            // Efficiently move data to local buffer
            while (context.Candidates.TryDequeue(out var c))
                localCandidates.Add(c);

            if (localCandidates.Count > 0)
            {
                context.Log($"[Analysis] Processing {localCandidates.Count} candidates...", LogLevel.Debug);

                // Always attempt analysis if we have data, regardless of count threshold (which is now 1 in Models)
                var currentWinner = analyzer.FindBestSaveDirectory(localCandidates, context.Settings, context.Game);
                
                if (currentWinner != null)
                {
                    if (string.Equals(currentWinner, lastWinner, StringComparison.OrdinalIgnoreCase))
                    {
                        stabilityCounter++;
                        context.Log($"[Analysis] Winner '{currentWinner}' stable for {stabilityCounter}/{REQUIRED_STABILITY} cycles.", LogLevel.Debug);
                        
                        // Acceleration: If we found it twice, we are pretty sure. 
                        // Next check should be immediate to finalize the process.
                        if (stabilityCounter >= 2)
                        {
                            currentDelay = 100; // Fast track
                        }
                    }
                    else
                    {
                        lastWinner = currentWinner;
                        stabilityCounter = 1;
                        context.Log($"[Analysis] New potential winner: {currentWinner}", LogLevel.Debug);
                        currentDelay = 15000; // Reset to slow poll for new candidate
                    }

                    if (stabilityCounter >= REQUIRED_STABILITY)
                    {
                        context.FinalPath = currentWinner;
                        context.Log($"[Analysis] Winner confirmed after {REQUIRED_STABILITY} stable cycles.", LogLevel.Info);
                        break;
                    }
                }
                else
                {
                    // If we lost the winner (e.g. scores shifted), reset
                    if (stabilityCounter > 0)
                    {
                        context.Log("[Analysis] Previous winner lost confidence/stability.", LogLevel.Debug);
                    }
                    stabilityCounter = 0;
                    lastWinner = null;
                    currentDelay = 15000; // Back to slow poll
                }
            }
            
            await Task.Delay(currentDelay, context.Token);
        }
    }
}
