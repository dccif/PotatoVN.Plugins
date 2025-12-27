using GalgameManager.Models;
using HarmonyLib;
using System.Reflection;

namespace PotatoVN.App.PluginBase.Patches;

[HarmonyPatch]
public class GalgameViewModelPatch
{
    [HarmonyTargetMethod]
    private static MethodInfo TargetMethod()
    {
        var type = AccessTools.TypeByName("GalgameManager.ViewModels.GalgameViewModel");
        return AccessTools.Method(type, "OnNavigatedTo");
    }

    [HarmonyPrefix]
    private static void Prefix(object parameter)
    {
        if (Plugin.Instance == null) return;

        // parameter is GalgamePageParameter
        if (parameter != null)
        {
            var fieldInfo = parameter.GetType().GetField("Galgame");
            if (fieldInfo != null)
            {
                var game = fieldInfo.GetValue(parameter) as Galgame;
                Plugin.Instance.CurrentGalgame = game;
            }
        }
    }
}