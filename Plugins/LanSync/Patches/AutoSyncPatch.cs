using GalgameManager.Models;
using HarmonyLib;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.PluginBase.Services;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase.Patches;

[HarmonyPatch]
public class AutoSyncPatch
{
    private static readonly HashSet<Guid> _syncedGames = new();

    [HarmonyTargetMethod]
    static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("GalgameManager.ViewModels.GalgameViewModel");
        return AccessTools.Method(type, "Play");
    }

    [HarmonyPrefix]
    static bool Prefix(object __instance)
    {
        // 1. Check if AutoSync is enabled
        if (Plugin.Instance?.Data?.AutoSync != true) return true;

        // Use reflection to get Item (Galgame)
        var itemProp = __instance.GetType().GetProperty("Item");
        if (itemProp == null) return true;

        var game = itemProp.GetValue(__instance) as Galgame;
        if (game == null) return true;

        // 2. Check if already synced for this session (prevent infinite loop)
        if (_syncedGames.Contains(game.Uuid))
        {
            _syncedGames.Remove(game.Uuid);
            return true;
        }

        // 3. Trigger Sync and Prevent original execution
        _ = SyncAndReplay(__instance);
        return false;
    }

    private static async Task SyncAndReplay(object viewModel)
    {
        var itemProp = viewModel.GetType().GetProperty("Item");
        var game = itemProp?.GetValue(viewModel) as Galgame;

        if (game == null) return;

        try
        {
            await SyncService.SyncGameAsync(game);

            _syncedGames.Add(game.Uuid);

            // Re-invoke Play on UI thread
            Plugin.Instance?.RunOnUI(async () =>
            {
                try
                {
                    var type = viewModel.GetType();
                    var method = AccessTools.Method(type, "Play");
                    var task = method.Invoke(viewModel, null) as Task;
                    if (task != null) await task;
                }
                catch (Exception e)
                {
                    Plugin.Instance.Notify(InfoBarSeverity.Error,
                        "AutoSync Replay Error", e.Message);
                }
            });
        }
        catch (Exception ex)
        {
            Plugin.Instance?.Notify(InfoBarSeverity.Error,
                "AutoSync Error", ex.Message);

            // Even if error, try to let the user play?
            _syncedGames.Add(game.Uuid);
            Plugin.Instance?.RunOnUI(async () =>
            {
                try
                {
                    var type = viewModel.GetType();
                    var method = AccessTools.Method(type, "Play");
                    var task = method.Invoke(viewModel, null) as Task;
                    if (task != null) await task;
                }
                catch { }
            });
        }
    }
}
