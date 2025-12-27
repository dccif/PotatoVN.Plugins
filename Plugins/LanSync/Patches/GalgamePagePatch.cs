using GalgameManager.Models;
using HarmonyLib;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Linq;
using System.Reflection;

namespace PotatoVN.App.PluginBase.Patches;

[HarmonyPatch]
public class GalgamePagePatch
{
    [HarmonyTargetMethod]
    private static MethodInfo TargetMethod()
    {
        // GalgamePage.xaml corresponds to the class GalgameManager.Views.HomeDetailPage
        var type = AccessTools.TypeByName("GalgameManager.Views.HomeDetailPage");
        return AccessTools.Method(type, "OnNavigatedTo");
    }

    [HarmonyPostfix]
    private static void Postfix(object __instance, object e)
    {
        try
        {
            if (__instance is not Page page) return;

            // Extract Galgame from NavigationEventArgs
            var game = GetGalgameFromEventArgs(e);

            // If game is null or no detected save path, do not show button
            if (game == null || string.IsNullOrWhiteSpace(game.DetectedSavePath?.ToPath())) return;

            // Try to add immediately if tree is ready
            if (!TryInjectButton(page, game))
            {
                // Capture game in closure for Loaded event
                RoutedEventHandler loadedHandler = null!;
                loadedHandler = (s, args) =>
                {
                    page.Loaded -= loadedHandler;
                    TryInjectButton(page, game);
                };
                page.Loaded += loadedHandler;
            }
        }
        catch
        {
            // Silent failure
        }
    }

    private static Galgame? GetGalgameFromEventArgs(object e)
    {
        if (e is NavigationEventArgs navArgs && navArgs.Parameter != null)
        {
            var param = navArgs.Parameter;
            // Parameter is likely GalgamePageParameter which has a 'Galgame' field
            // Or it could be the Galgame object itself depending on navigation
            if (param is Galgame g) return g;

            var fieldInfo = param.GetType().GetField("Galgame");
            if (fieldInfo != null) return fieldInfo.GetValue(param) as Galgame;

            // Fallbacks for properties
            var propInfo = param.GetType().GetProperty("Galgame");
            if (propInfo != null) return propInfo.GetValue(param) as Galgame;
        }

        return null;
    }

    private static bool TryInjectButton(Page page, Galgame game)
    {
        try
        {
            var commandBar = FindCommandBar(page);
            if (commandBar != null)
            {
                AddButton(commandBar, game);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static void AddButton(CommandBar commandBar, Galgame game)
    {
        // Avoid duplicate
        if (commandBar.PrimaryCommands.Any(c => c is AppBarButton btn && (string)btn.Tag == "LanSyncButton")) return;

        var btn = new AppBarButton
        {
            Label = Plugin.GetLocalized("Ui_LanSync") ?? "LanSync",
            Icon = new FontIcon { Glyph = "\uE895" },
            Tag = "LanSyncButton"
        };

        btn.Click += async (s, args) =>
        {
            // Use the captured game instance
            await Services.SyncService.SyncGameAsync(game);
        };

        // Insert after "Play" button (Start Game)
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