using System;
using System.Reflection;
using System.Threading.Tasks;
using GalgameManager.WinApp.Base.Contracts;
using GalgameManager.WinApp.Base.Contracts.PluginUi;
using GalgameManager.WinApp.Base.Models;
using HarmonyLib;
using Microsoft.Windows.AppLifecycle;
using PotatoVN.App.PluginBase.Models;
using Windows.ApplicationModel.Activation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using GalgameManager.Models;

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

            try
            {
                _harmony = new Harmony("com.potatovn.plugin.protocol");
                var assembly = Assembly.Load("GalgameManager");

                // Hook JumpListActivationHandler.CanHandleInternal
                var handlerType = assembly.GetType("GalgameManager.Activation.JumpListActivationHandler");
                if (handlerType != null)
                {
                    var original = AccessTools.Method(handlerType, "CanHandleInternal");
                    var prefix = AccessTools.Method(typeof(Plugin), nameof(CanHandleInternalPrefix));
                    
                    if (original != null && prefix != null)
                    {
                        _harmony.Patch(original, prefix: new HarmonyMethod(prefix));
                    }
                }

                // Hook HomeViewModel.GalFlyout_Opening
                var vmType = assembly.GetType("GalgameManager.ViewModels.HomeViewModel");
                if (vmType != null)
                {
                    var original = AccessTools.Method(vmType, "GalFlyout_Opening");
                    var postfix = AccessTools.Method(typeof(Plugin), nameof(GalFlyoutOpeningPostfix));
                    if (original != null && postfix != null)
                    {
                        _harmony.Patch(original, postfix: new HarmonyMethod(postfix));
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Plugin Error: GalFlyout_Opening method not found on HomeViewModel.");
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

        private static void GalFlyoutOpeningPostfix(object sender)
        {
            try 
            {
                if (sender is MenuFlyout flyout)
                {
                    // Get the Galgame from flyout.Target.DataContext
                    object? game = null;
                    if (flyout.Target is FrameworkElement target && target.DataContext != null)
                    {
                         game = target.DataContext;
                    }
                    
                    InjectOrUpdateMenuItem(flyout, game);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GalFlyoutOpeningPostfix Error: {ex}");
            }
        }

        private static void InjectOrUpdateMenuItem(MenuFlyout flyout, object? game)
        {
            MenuFlyoutItem? targetItem = null;

            // Check if already added
            foreach(var item in flyout.Items)
            {
                if (item.Tag as string == "Plugin_CreateShortcut" && item is MenuFlyoutItem mfi) 
                {
                    targetItem = mfi;
                    break;
                }
            }

            if (targetItem == null)
            {
                // Create new item
                targetItem = new MenuFlyoutItem
                {
                    Text = "创建桌面快捷方式", 
                    Icon = new SymbolIcon(Symbol.Link),
                    Tag = "Plugin_CreateShortcut"
                };
                targetItem.Click += CreateShortcut_Click;

                // Find insertion point
                int insertIndex = -1;
                
                // Strategy 1: Check Icon (Symbol.OpenLocal)
                for (int i = 0; i < flyout.Items.Count; i++)
                {
                    if (flyout.Items[i] is MenuFlyoutItem existingItem)
                    {
                        if (existingItem.Icon is SymbolIcon symIcon && symIcon.Symbol == Symbol.OpenLocal)
                        {
                            insertIndex = i + 1;
                            break;
                        }
                    }
                }

                // Strategy 2: Check Command (OpenGameInExplorerCommand)
                if (insertIndex == -1)
                {
                    try
                    {
                        var appType = Type.GetType("GalgameManager.App, GalgameManager");
                        var getServiceMethod = appType?.GetMethod("GetService", BindingFlags.Public | BindingFlags.Static);
                        var homeViewModelType = Type.GetType("GalgameManager.ViewModels.HomeViewModel, GalgameManager");

                        if (getServiceMethod != null && homeViewModelType != null)
                        {
                            var viewModel = getServiceMethod.MakeGenericMethod(homeViewModelType).Invoke(null, null);
                            if (viewModel != null)
                            {
                                var commandProp = homeViewModelType.GetProperty("OpenGameInExplorerCommand");
                                var targetCommand = commandProp?.GetValue(viewModel);

                                if (targetCommand != null)
                                {
                                    for (int i = 0; i < flyout.Items.Count; i++)
                                    {
                                        if (flyout.Items[i] is MenuFlyoutItem menuItem && menuItem.Command == targetCommand)
                                        {
                                            insertIndex = i + 1;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore reflection errors in strategy 2
                    }
                }

                if (insertIndex != -1 && insertIndex <= flyout.Items.Count)
                {
                    flyout.Items.Insert(insertIndex, targetItem);
                }
                else
                {
                    // Fallback: Add to end
                    flyout.Items.Add(targetItem);
                }
            }

            // Update DataContext for the current open session
            targetItem.DataContext = game;
            // Optionally disable if game is null
            targetItem.IsEnabled = game != null;
        }

        private static void CreateShortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement item && item.DataContext != null)
                {
                    var game = item.DataContext;
                    
                    // Get UUID, Name, and ExePath
                    var uuidObj = game.GetType().GetProperty("Uuid")?.GetValue(game);
                    var nameObj = game.GetType().GetProperty("Name")?.GetValue(game);
                    var exePathObj = game.GetType().GetProperty("ExePath")?.GetValue(game);
                    
                    if (uuidObj is Guid uuid && nameObj is LockableProperty<string> name)
                    {
                        var appExePath = Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(appExePath))
                        {
                            string? gameExePath = exePathObj as string;
                            // If gameExePath is null or empty, it defaults to app icon in helper
                            CreateShortcutOnDesktop(name.Value!, appExePath, $"potato-vn:{uuid}", gameExePath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CreateShortcut Click Error: {ex}");
            }
        }

        private static void CreateShortcutOnDesktop(string name, string targetPath, string arguments, string? iconPath)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) name = "GalgameShortcut";
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                
                // Sanitize filename
                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                {
                    name = name.Replace(c, '_');
                }
                
                // Determine source for icon extraction
                // If iconPath (Game Exe) is valid, use it. Otherwise use targetPath (App Exe).
                bool usingGameExe = !string.IsNullOrEmpty(iconPath) && System.IO.File.Exists(iconPath);
                string sourceIconPath = usingGameExe ? iconPath! : targetPath;
                
                string destIconPath = "";
                bool iconSaved = false;

                // 1. Try to save in the same directory as the game exe
                if (usingGameExe)
                {
                    try
                    {
                        string? gameDir = System.IO.Path.GetDirectoryName(sourceIconPath);
                        if (!string.IsNullOrEmpty(gameDir))
                        {
                            string potentialPath = System.IO.Path.Combine(gameDir, $"{name}.ico");
                            
                            using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(sourceIconPath))
                            {
                                if (icon != null)
                                {
                                    using (var stream = new System.IO.FileStream(potentialPath, System.IO.FileMode.Create))
                                    {
                                        icon.Save(stream);
                                    }
                                    destIconPath = potentialPath;
                                    iconSaved = true;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to save icon to game directory: {ex.Message}. Falling back to AppData.");
                    }
                }

                // 2. Fallback: Save to AppData if not saved yet (failed or using App Exe)
                if (!iconSaved)
                {
                    try
                    {
                        string iconStorage = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PotatoVN", "ShortcutIcons");
                        System.IO.Directory.CreateDirectory(iconStorage);
                        destIconPath = System.IO.Path.Combine(iconStorage, $"{name}.ico");

                        using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(sourceIconPath))
                        {
                            if (icon != null)
                            {
                                using (var stream = new System.IO.FileStream(destIconPath, System.IO.FileMode.Create))
                                {
                                    icon.Save(stream);
                                }
                                iconSaved = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                         Debug.WriteLine($"Failed to save icon to AppData: {ex.Message}");
                         // Final fallback: just point to the exe itself if extraction fails completely
                         destIconPath = sourceIconPath;
                    }
                }

                // Create .url file (Internet Shortcut)
                string shortcutPath = System.IO.Path.Combine(desktopPath, $"{name}.url");
                string url = arguments; // The protocol is passed as arguments

                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(shortcutPath))
                {
                    writer.WriteLine("[InternetShortcut]");
                    writer.WriteLine($"URL={url}");
                    writer.WriteLine("IDList=");
                    writer.WriteLine("IconIndex=0");
                    writer.WriteLine($"IconFile={destIconPath}");
                    writer.WriteLine("[{000214A0-0000-0000-C000-000000000046}]");
                    writer.WriteLine("Prop3=19,0");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Create Shortcut Error: {ex}");
            }
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
