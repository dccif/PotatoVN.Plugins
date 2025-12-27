using PotatoVN.App.PluginBase.SaveDetection.Analyzers;
using PotatoVN.App.PluginBase.SaveDetection.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase.SaveDetection.Pipeline;

internal class AnalysisStep : IDetectionStep
{
    private const int STARTUP_GRACE_PERIOD_MS = 10000;
    private const int REQUIRED_STABILITY_CYCLES = 3;

    public async Task ExecuteAsync(DetectionContext context)
    {
        context.Log("Starting AnalysisStep...", LogLevel.Debug);

        var analyzer = new VotingAnalyzer
        {
            Logger = (msg, level) => context.Log(msg, level)
        };

        if (context.Game != null)
        {
            context.Log("[Analysis] Pre-computing game variants...", LogLevel.Debug);
            analyzer.Prepare(context.Game);
        }

        context.Log($"[Analysis] Waiting {STARTUP_GRACE_PERIOD_MS}ms for startup noise to settle...", LogLevel.Debug);
        await Task.Delay(STARTUP_GRACE_PERIOD_MS, context.Token);

        int dropped = 0;
        while (context.Candidates.TryDequeue(out _)) dropped++;
        if (dropped > 0)
            context.Log($"[Analysis] Dropped {dropped} candidates from startup grace period.", LogLevel.Debug);

        var currentBatch = new List<PathCandidate>();
        string? candidatePath = null;
        int stabilityCounter = 0;
        int currentCycleDelay = 5000;

        while (!context.Token.IsCancellationRequested && !context.TargetProcess.HasExited)
        {
            // 1. Adaptive Wait
            await Task.Delay(currentCycleDelay, context.Token);

            // Update delay for next cycle (5s -> 2s -> 1s -> 1s...)
            if (currentCycleDelay > 2000) currentCycleDelay = 2000;
            else if (currentCycleDelay > 1000) currentCycleDelay = 1000;

            // 2. Harvest fresh data
            currentBatch.Clear();
            bool hasNewData = false;
            while (context.Candidates.TryDequeue(out var c))
            {
                currentBatch.Add(c);
                hasNewData = true;
            }

            string? currentCycleWinner = null;

            if (hasNewData)
            {
                context.Log($"[Analysis] Processing batch of {currentBatch.Count} accumulated candidates...", LogLevel.Debug);
                currentCycleWinner = analyzer.FindBestSaveDirectory(currentBatch, context.Settings, context.Game);
            }

            // 3. Strict Confirmation Logic
            if (currentCycleWinner != null)
            {
                if (stabilityCounter == 0)
                {
                    candidatePath = currentCycleWinner;
                    stabilityCounter = 1;
                    context.Log($"[Analysis] Cycle 1: Potential candidate found: {candidatePath}. Stability: 1/{REQUIRED_STABILITY_CYCLES}", LogLevel.Debug);
                }
                else
                {
                    if (string.Equals(currentCycleWinner, candidatePath, StringComparison.OrdinalIgnoreCase))
                    {
                        stabilityCounter++;
                        context.Log($"[Analysis] Cycle {stabilityCounter}: Candidate verified again: {candidatePath}. Stability: {stabilityCounter}/{REQUIRED_STABILITY_CYCLES}", LogLevel.Debug);
                    }
                    else
                    {
                        context.Log($"[Analysis] Mismatch! Expected '{candidatePath}', got '{currentCycleWinner}'. Resetting stability.", LogLevel.Warning);
                        candidatePath = currentCycleWinner;
                        stabilityCounter = 1;
                        context.Log($"[Analysis] Cycle 1: New potential candidate: {candidatePath}. Stability: 1/{REQUIRED_STABILITY_CYCLES}", LogLevel.Debug);
                    }
                }
            }
            else
            {
                if (stabilityCounter > 0)
                {
                    context.Log($"[Analysis] Cycle failed (No valid winner/Silence). Resetting stability from {stabilityCounter} to 0.", LogLevel.Debug);
                    stabilityCounter = 0;
                    candidatePath = null;
                }
            }

            // 4. Confirmation
            if (stabilityCounter >= REQUIRED_STABILITY_CYCLES && candidatePath != null)
            {
                context.FinalPath = candidatePath;
                context.Log($"[Analysis] Winner confirmed after {REQUIRED_STABILITY_CYCLES} strict cycles: {candidatePath}", LogLevel.Info);
                break;
            }
        }
    }
}