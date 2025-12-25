using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PotatoVN.App.PluginBase.SaveDetection.Models;

namespace PotatoVN.App.PluginBase.SaveDetection;

public interface ISaveCandidateProvider
{
    // 这里的 Filter 委托实现了“早过滤”逻辑
    // Updated to include IoOperation for context-aware filtering
    Task StartAsync(DetectionContext context, Func<string, IoOperation, bool> pathFilter);
    void Stop();
}

public interface ISavePathAnalyzer
{
    bool IsValidPath(string path, SaveDetectorOptions options, GalgameManager.Models.Galgame? game = null, IoOperation op = IoOperation.Unknown);
    string? FindBestSaveDirectory(List<PathCandidate> candidates, SaveDetectorOptions options, GalgameManager.Models.Galgame? game = null);
}

public interface IDetectionStep { Task ExecuteAsync(DetectionContext context); }

