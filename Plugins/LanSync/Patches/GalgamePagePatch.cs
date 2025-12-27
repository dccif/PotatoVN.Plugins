using HarmonyLib;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using System.Reflection;

namespace PotatoVN.App.PluginBase.Patches;

[HarmonyPatch]
public class GalgamePagePatch
{
    [HarmonyTargetMethod]
    private static MethodInfo TargetMethod()
    {
        var type = AccessTools.TypeByName("GalgameManager.Views.HomeDetailPage");
        return AccessTools.Method(type, "OnNavigatedTo");
    }

    [HarmonyPostfix]
    private static void Postfix(object __instance)
    {
        try
        {
            if (__instance is not Page page) return;

            // Try to add immediately if tree is ready
            if (!TryInjectButton(page))
                // If not found (e.g. tree not ready), wait for Loaded
                page.Loaded += Page_Loaded;
        }
        catch
        {
            // Silent failure or log
        }
    }

    private static void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Page page)
        {
            page.Loaded -= Page_Loaded;
            TryInjectButton(page);
        }
    }

    private static bool TryInjectButton(Page page)
    {
        try
        {
            var commandBar = FindCommandBar(page);
            if (commandBar != null)
            {
                AddButton(commandBar);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static void AddButton(CommandBar commandBar)
    {
        // Avoid duplicate
        if (commandBar.PrimaryCommands.Any(c => c is AppBarButton btn && (string)btn.Tag == "LanSyncButton")) return;

        // Check if detected save path is present
        var game = Plugin.Instance?.CurrentGalgame;
        if (game == null || string.IsNullOrWhiteSpace(game.DetectedSavePath?.ToPath())) return;

        var btn = new AppBarButton
        {
            Label = Plugin.GetLocalized("Ui_LanSync") ?? "LanSync",
            Icon = new FontIcon { Glyph = "\uE895" },
            Tag = "LanSyncButton"
        };

        btn.Click += async (s, e) =>
        {
            var currentGame = Plugin.Instance?.CurrentGalgame;
            if (currentGame == null) return;

            // Disable button briefly? Or show InfoBar is handled by SyncService.
            await Services.SyncService.SyncGameAsync(currentGame);
        };

        // Insert after "Play" button (Start Game)
        // In the XAML, Play is the first item in CommandBar content (PrimaryCommands).
        // <AppBarButton x:Uid="GalgamePage_Play" ...> is usually index 0.
        // We want to insert at index 1.
        if (commandBar.PrimaryCommands.Count > 0)
            commandBar.PrimaryCommands.Insert(1, btn);
        else
            commandBar.PrimaryCommands.Add(btn);
    }

    private static CommandBar? FindCommandBar(DependencyObject parent)
    {
        if (parent == null) return null;

        // 1. Try Visual Tree (most robust if loaded)
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is CommandBar bar) return bar;

            // Recursive
            var res = FindCommandBar(child);
            if (res != null) return res;
        }

        // 2. Fallback to Logical Content (for simple nesting before Loaded)
        if (count == 0 && parent is ContentControl cc && cc.Content is DependencyObject content)
        {
            if (content is CommandBar bar) return bar;
            return FindCommandBar(content);
        }

        if (count == 0 && parent is Panel panel)
            foreach (var child in panel.Children)
            {
                if (child is CommandBar bar) return bar;
                if (child is DependencyObject depChild)
                {
                    var res = FindCommandBar(depChild);
                    if (res != null) return res;
                }
            }

        return null;
    }
}