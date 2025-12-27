using HarmonyLib;
using PotatoVN.App.PluginBase.Models;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase;

public static class PluginPatch
{
    private static object? _settingsService;
    private static Type? _settingsServiceType;
    private static PluginData? _data;
    private static string? _autoDetectKey;
    private static Harmony? _harmony;

    public static async Task InitializeAsync(PluginData data)
    {
        _data = data;
        try
        {
            // 1. 获取程序集和类型
            Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "GalgameManager");
            Type? appType = assembly?.GetType("GalgameManager.App");
            Type? interfaceType = assembly?.GetType("GalgameManager.Contracts.Services.ILocalSettingsService");
            Type? keyValuesType = assembly?.GetType("GalgameManager.Enums.KeyValues");

            if (appType == null || interfaceType == null || keyValuesType == null) return;

            // 2. 获取服务实例 (App.GetService<ILocalSettingsService>())
            var getServiceMethod = appType.GetMethod("GetService", BindingFlags.Public | BindingFlags.Static)?.MakeGenericMethod(interfaceType);
            _settingsService = getServiceMethod?.Invoke(null, null);
            if (_settingsService == null) return;
            _settingsServiceType = _settingsService.GetType();

            // 3. 获取 Key 字符串
            _autoDetectKey = keyValuesType.GetField("AutoDetectSavePath", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
            if (string.IsNullOrEmpty(_autoDetectKey)) return;

            var saveMethod = _settingsServiceType.GetMethod("SaveSettingAsync")?.MakeGenericMethod(typeof(bool));
            if (saveMethod == null) return;

            // 4. 只有当没有记录原始值时，才去读取并记录
            if (_data.OriginalAutoDetectValue == null)
            {
                var readMethod = _settingsServiceType.GetMethod("ReadSettingAsync")?.MakeGenericMethod(typeof(bool));
                if (readMethod != null)
                {
                    dynamic task = readMethod.Invoke(_settingsService, [_autoDetectKey, false, null, false])!;
                    bool? currentVal = await task;

                    // 保存原始值，如果读取失败默认认为它是 true (保持原有行为逻辑)
                    _data.OriginalAutoDetectValue = currentVal ?? true;

                    // 如果原值是 true，则改为 false
                    if (currentVal == true)
                    {
                        await (Task)saveMethod.Invoke(_settingsService, [_autoDetectKey, false, false, false, null, false])!;
                    }
                }
            }
            else
            {
                // 如果已经有记录，确保当前设置是 false (因为插件正在运行)
                await (Task)saveMethod.Invoke(_settingsService, [_autoDetectKey, false, false, false, null, false])!;
            }

            // 5. Harmony Hook
            _harmony = new Harmony("PotatoVN.App.PluginBase.SettingsViewModelPatch");
            Type? settingsVmType = assembly?.GetType("GalgameManager.ViewModels.SettingsViewModel");
            MethodInfo? onNavigatedTo = settingsVmType?.GetMethod("OnNavigatedTo");
            if (settingsVmType != null && onNavigatedTo != null)
            {
                var postfix = typeof(PluginPatch).GetMethod(nameof(SettingsViewModel_OnNavigatedTo_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(onNavigatedTo, postfix: new HarmonyMethod(postfix));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Plugin] PluginPatch Init Error: {ex}");
        }
    }

    private static void SettingsViewModel_OnNavigatedTo_Postfix(object __instance)
    {
        try
        {
            if (_settingsService == null || string.IsNullOrEmpty(_autoDetectKey)) return;

            var readMethod = _settingsServiceType?.GetMethod("ReadSettingAsync")?.MakeGenericMethod(typeof(bool));
            if (readMethod != null)
            {
                // 重新读取配置
                // 注意：这里需要阻塞等待结果，或者确保它是异步安全的。
                // 由于 ReadSettingAsync 返回 Task<bool>，直接 .Result 可能会死锁，但原代码 SettingsViewModel 构造函数里就是直接调用的 .Result。
                // 为了保险，我们尝试反射调用 Result 属性。
                dynamic task = readMethod.Invoke(_settingsService, [_autoDetectKey, false, null, false])!;
                bool value = task.Result;

                // 设置 ViewModel 的 AutoDetectSavePath 属性
                var prop = __instance.GetType().GetProperty("AutoDetectSavePath");
                prop?.SetValue(__instance, value);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Plugin] SettingsViewModel Postfix Error: {ex}");
        }
    }

    public static async Task RestoreAsync()
    {
        // 只有当有原始值记录时才恢复
        if (_data?.OriginalAutoDetectValue.HasValue == true && _settingsService != null && _autoDetectKey != null)
        {
            var originalValue = _data.OriginalAutoDetectValue.Value;

            try
            {
                var saveMethod = _settingsServiceType?.GetMethod("SaveSettingAsync")?.MakeGenericMethod(typeof(bool));
                if (saveMethod != null)
                {
                    // 1. 恢复后端设置
                    await (Task)saveMethod.Invoke(_settingsService, [_autoDetectKey, originalValue, false, false, null, false])!;
                }

                // 2. 尝试清空 Frame 的缓存 (使用 UiThreadInvokeHelper)
                // 这样当用户下次导航回 SettingsPage 时，会创建一个新实例并读取正确的配置
                Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "GalgameManager");
                if (assembly != null)
                {
                    Type? helperType = assembly.GetType("GalgameManager.Helpers.UiThreadInvokeHelper");
                    MethodInfo? invokeMethod = helperType?.GetMethod("InvokeAsync", BindingFlags.Public | BindingFlags.Static, null, [typeof(Action)], null);

                    if (invokeMethod != null)
                    {
                        Action uiAction = () =>
                        {
                            try
                            {
                                // 获取 App 类型
                                Type? appType = assembly.GetType("GalgameManager.App");
                                if (appType == null) return;

                                // 获取 NavigationService
                                Type? navServiceType = assembly.GetType("GalgameManager.Contracts.Services.INavigationService");
                                if (navServiceType == null) return;

                                var getNavServiceMethod = appType.GetMethod("GetService", BindingFlags.Public | BindingFlags.Static)?.MakeGenericMethod(navServiceType);
                                var navService = getNavServiceMethod?.Invoke(null, null);
                                if (navService == null) return;

                                // 获取 Frame
                                var frameProp = navServiceType.GetProperty("Frame");
                                var frame = frameProp?.GetValue(navService);
                                if (frame == null) return;

                                // 清空 Frame 的缓存
                                var cacheSizeProp = frame.GetType().GetProperty("CacheSize");
                                if (cacheSizeProp != null)
                                {
                                    // 获取当前 CacheSize (默认为 10)
                                    int originalSize = (int)cacheSizeProp.GetValue(frame)!;
                                    // 设置为 0 以清空缓存 (丢弃所有缓存的页面实例)
                                    cacheSizeProp.SetValue(frame, 0);
                                    // 恢复原始大小
                                    cacheSizeProp.SetValue(frame, originalSize);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Plugin] UI Restore Inner Error: {ex}");
                            }
                        };

                        await (Task)invokeMethod.Invoke(null, [uiAction])!;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Plugin] Restore Error: {ex}");
            }

            // 清除原始值记录
            _data.OriginalAutoDetectValue = null;
        }

        _harmony?.UnpatchAll("PotatoVN.App.PluginBase.SettingsViewModelPatch");
    }
}
