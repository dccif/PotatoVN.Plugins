using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PotatoVN.App.PluginBase.SaveDetection.Analyzers;
using PotatoVN.App.PluginBase.SaveDetection.Models;

namespace PotatoVN.App.PluginBase.SaveDetection.Pipeline;

internal class AnalysisStep : IDetectionStep
{
    public async Task ExecuteAsync(DetectionContext context)
    {
        var analyzer = new VotingAnalyzer();
        var localCandidates = new List<PathCandidate>();

        while (!context.Token.IsCancellationRequested && !context.TargetProcess.HasExited)
        {
            // 高效移动数据到本地缓冲区
            while (context.Candidates.TryDequeue(out var c)) 
                localCandidates.Add(c);

            if (localCandidates.Count >= context.Settings.MinVoteCountThreshold)
            {
                var result = analyzer.FindBestSaveDirectory(localCandidates, context.Settings, context.Game);
                if (result != null) { context.FinalPath = result; break; }
            }
            await Task.Delay(context.Settings.AnalysisIntervalMs, context.Token);
        }
    }
}
