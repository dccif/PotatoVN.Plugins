using System.Threading.Tasks;
using PotatoVN.App.PluginBase.SaveDetection.Analyzers;
using PotatoVN.App.PluginBase.SaveDetection.Models;
using PotatoVN.App.PluginBase.SaveDetection.Providers;

namespace PotatoVN.App.PluginBase.SaveDetection.Pipeline;

internal class DiscoveryStep : IDetectionStep
{
    public async Task ExecuteAsync(DetectionContext context)
    {
        var analyzer = new VotingAnalyzer();
        
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
        await provider.StartAsync(context, path => analyzer.IsValidPath(path, context.Settings, context.Game));
    }
}
