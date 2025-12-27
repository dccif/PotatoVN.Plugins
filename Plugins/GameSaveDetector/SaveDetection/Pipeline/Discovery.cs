using PotatoVN.App.PluginBase.SaveDetection.Analyzers;
using PotatoVN.App.PluginBase.SaveDetection.Models;
using PotatoVN.App.PluginBase.SaveDetection.Providers;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase.SaveDetection.Pipeline;

internal class DiscoveryStep : IDetectionStep
{
    public async Task ExecuteAsync(DetectionContext context)
    {
        context.Log("Starting DiscoveryStep...", LogLevel.Debug);
        var analyzer = new VotingAnalyzer
        {
            Logger = (msg, level) => context.Log(msg, level)
        };

        // If Admin, use ETW. Else use FileSystemWatcher.
        ISaveCandidateProvider provider;

        if (context.Settings.AllowEtw && SaveDetector.IsAdministrator())
        {
            provider = new EtwProvider();
        }
        else
        {
            // FileSystemWatcherProvider requires Game context which we added to DetectionContext
            provider = new WatcherProvider();
        }

        context.Log($"Using Provider: {provider.GetType().Name}");
        context.ActiveProvider = provider;

        // 核心：在 Provider 内部实现早过滤
        // Updated to pass operation type for smarter filtering (e.g. accepting Renames even if extension is unknown)
        await provider.StartAsync(context, (path, op) => analyzer.IsValidPath(path, context.Settings, context.Game, op));
    }
}
