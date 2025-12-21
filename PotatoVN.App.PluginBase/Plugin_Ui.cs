using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using PotatoVN.App.PluginBase.Controls;
using PotatoVN.App.PluginBase.Controls.Prefabs;
using PotatoVN.App.PluginBase.Helper;

namespace PotatoVN.App.PluginBase;

public partial class Plugin
{
    public FrameworkElement CreateSettingUi()
    {
        StdStackPanel panel = new();      

        Button bigScreenBtn = new() { Content = GetLocalized("EnterBigScreen") ?? "Enter Big Screen Mode" };
        bigScreenBtn.Click += (_, _) =>
        {
            var games = _hostApi.GetAllGames();
            var pluginPath = _hostApi.GetPluginPath();
            var window = new Views.BigScreenWindow(games, pluginPath);
            window.Activate();
        };
        panel.Children.Add(new StdSetting(GetLocalized("BigScreenTitle") ?? "Big Screen", 
            GetLocalized("BigScreenDesc") ?? "Enter Big Screen Mode", bigScreenBtn).WarpWithPanel());
        return panel;
    }
}