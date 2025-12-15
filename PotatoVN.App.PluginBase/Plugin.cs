using System;
using System.Reflection;
using System.Threading.Tasks;
using GalgameManager.WinApp.Base.Contracts;
using GalgameManager.WinApp.Base.Models;
using HarmonyLib;
using Microsoft.Windows.AppLifecycle;
using PotatoVN.App.PluginBase.Models;
using Windows.ApplicationModel.Activation;

namespace PotatoVN.App.PluginBase
{
    public partial class Plugin : IPlugin
    {
        private IPotatoVnApi _hostApi = null!;
        private PluginData _data = new ();
        private Harmony? _harmony;
        
        public PluginInfo Info { get; } = new()
        {
            Id = new Guid("9e3286fe-2f75-4b7a-8951-f8d68b304eb0"),
            Name = "协议启动支持",
            Description = "支持通过 'potato-vn:' 协议启动游戏。",
        };

        public async Task InitializeAsync(IPotatoVnApi hostApi)
        {
            _hostApi = hostApi;
            XamlResourceLocatorFactory.packagePath = _hostApi.GetPluginPath();
            var dataJson = await _hostApi.GetDataAsync();
            if (!string.IsNullOrWhiteSpace(dataJson))
            {
                try
                {
                    _data = System.Text.Json.JsonSerializer.Deserialize<PluginData>(dataJson) ?? new PluginData();
                }
                catch
                {
                    _data = new PluginData();
                }
            }
            _data.PropertyChanged += (_, _) => SaveData();

            // Hook JumpListActivationHandler.CanHandleInternal
            try
            {
                _harmony = new Harmony("com.potatovn.plugin.protocol");
                
                var assembly = Assembly.Load("GalgameManager");
                var handlerType = assembly.GetType("GalgameManager.Activation.JumpListActivationHandler");
                
                if (handlerType != null)
                {
                    var original = AccessTools.Method(handlerType, "CanHandleInternal");
                    var prefix = AccessTools.Method(typeof(Plugin), nameof(CanHandleInternalPrefix));
                    
                    if (original != null && prefix != null)
                    {
                        _harmony.Patch(original, prefix: new HarmonyMethod(prefix));
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Plugin Error: CanHandleInternal or Prefix not found.");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Plugin Hook Error: {ex}");
            }
        }
        
        private static bool CanHandleInternalPrefix(object args, object __instance, ref bool __result)
        {
            try
            {
                if (args is AppActivationArguments appArgs)
                {
                    if (appArgs.Kind == ExtendedActivationKind.Protocol && appArgs.Data is IProtocolActivatedEventArgs protocolArgs)
                    {
                        var uri = protocolArgs.Uri;
                        if (uri.Scheme == "potato-vn")
                        {
                            // Parse UUID
                            // Support: potato-vn:UUID and potato-vn://UUID
                            string uuidStr = uri.OriginalString;
                            
                            if (uuidStr.StartsWith("potato-vn://"))
                            {
                                uuidStr = uuidStr.Substring("potato-vn://".Length);
                            }
                            else if (uuidStr.StartsWith("potato-vn:"))
                            {
                                uuidStr = uuidStr.Substring("potato-vn:".Length);
                            }

                            // Clean up
                            uuidStr = uuidStr.TrimEnd('/');

                            if (Guid.TryParse(uuidStr, out var guid))
                            {
                                // Logic replacement
                                var traverse = Traverse.Create(__instance);
                                var collectionService = traverse.Field("_galgameCollectionService").GetValue();
                                
                                if (collectionService != null)
                                {
                                    MethodInfo? method = collectionService.GetType().GetMethod("GetGalgameFromUuid");
                                    var game = method?.Invoke(collectionService, new object[] { guid });
                                    
                                    if (game != null)
                                    {
                                        traverse.Field("_game").SetValue(game);
                                        __result = true; // CanHandle = true
                                        return false; // Skip original execution
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Prefix logic error: {ex}");
            }
            return true; // Continue original execution
        }
        
        private void SaveData()
        {
            var dataJson = System.Text.Json.JsonSerializer.Serialize(_data);
            _ = _hostApi.SaveDataAsync(dataJson);
        }

        protected Guid Id => Info.Id;

        /// <summary>
        /// 插件卸载时清理资源
        /// </summary>
        public void Dispose()
        {
            try
            {
                _harmony?.UnpatchAll("com.potatovn.plugin.protocol");
            }
            catch
            {
                // 忽略卸载时的异常
            }
        }
    }
}
