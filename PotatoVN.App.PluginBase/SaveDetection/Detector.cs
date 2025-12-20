using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using PotatoVN.App.PluginBase.SaveDetection.Models;
using PotatoVN.App.PluginBase.SaveDetection.Pipeline;

namespace PotatoVN.App.PluginBase.SaveDetection;

public static class SaveDetector
{
    private static ISaveDetectorLogger _logger = new NullLogger();
    private static SaveDetectorOptions _options = new();

    public static void Configure(ISaveDetectorLogger logger, SaveDetectorOptions? options = null)
    {
        _logger = logger;
        if (options != null) _options = options;
    }

    public static async Task<string?> DetectAsync(Process process, CancellationToken token, GalgameManager.Models.Galgame? game = null, ISaveDetectorLogger? logger = null, SaveDetectorOptions? options = null)
    {
        var context = new DetectionContext(process, token, logger ?? _logger, options ?? _options)
        {
            Game = game
        };

        var pipeline = new List<IDetectionStep> { new DiscoveryStep(), new AnalysisStep() };

        try
        {
            foreach (var step in pipeline)
            {
                if (token.IsCancellationRequested || context.FinalPath != null) break;
                await step.ExecuteAsync(context);
            }
        }
        finally
        {
            // Cleanup provider
            context.ActiveProvider?.Stop();
            if (context.ActiveProvider is IDisposable d) d.Dispose();
        }
        return context.FinalPath;
    }

    internal static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
