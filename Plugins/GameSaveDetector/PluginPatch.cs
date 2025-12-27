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

    // 标记是否处于“等待恢复”状态
    private static bool _isRestoring;
    // 暂存需要恢复的原始值
    private static bool? _pendingRestoreValue;

    public static async Task InitializeAsync(PluginData data)
    {
        _data = data;
        _isRestoring = false;
        _pendingRestoreValue = null;

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

            // --- 延迟恢复逻辑 (Lazy Restore) ---
            // 如果处于恢复模式，说明用户之前卸载了插件但 SettingsPage 被缓存了
            // 此时我们需要将值恢复为原始值，然后移除 Hook
            if (_isRestoring)
            {
                if (_pendingRestoreValue.HasValue)
                {
                    var prop = __instance.GetType().GetProperty("AutoDetectSavePath");
                    prop?.SetValue(__instance, _pendingRestoreValue.Value);
                }

                // 恢复完成，移除所有 Patch 并重置状态
                _harmony?.UnpatchAll("PotatoVN.App.PluginBase.SettingsViewModelPatch");
                _isRestoring = false;
                _pendingRestoreValue = null;
                return;
            }
            // ------------------------------------

            var readMethod = _settingsServiceType?.GetMethod("ReadSettingAsync")?.MakeGenericMethod(typeof(bool));
            if (readMethod != null)
            {
                // 重新读取配置
                dynamic task = readMethod.Invoke(_settingsService, [_autoDetectKey, false, null, false])!;
                bool value = task.Result;

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
            _pendingRestoreValue = originalValue;

            try
            {
                var saveMethod = _settingsServiceType?.GetMethod("SaveSettingAsync")?.MakeGenericMethod(typeof(bool));
                if (saveMethod != null)
                {
                    // 1. 恢复后端设置
                    await (Task)saveMethod.Invoke(_settingsService, [_autoDetectKey, originalValue, false, false, null, false])!;
                }

                // 2. 标记恢复模式
                // 由于用户卸载插件时通常不在 SettingsPage，我们不需要立即更新 UI。
                // 我们只需设置此标记，当用户下次导航到 SettingsPage (触发 OnNavigatedTo) 时，
                // Harmony Postfix 会自动检测此标记，更新 UI 上的值，并执行 UnpatchAll。
                _isRestoring = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Plugin] Restore Error: {ex}");
            }

            // 清除原始值记录
            _data.OriginalAutoDetectValue = null;
        }
        else
        {
            // 如果没有需要恢复的值，直接清理 Patch
            _harmony?.UnpatchAll("PotatoVN.App.PluginBase.SettingsViewModelPatch");
        }
    }
}
