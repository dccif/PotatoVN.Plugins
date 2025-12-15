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
        panel.Children.Add(new UserControl1().WarpWithPanel());
        panel.Children.Add(new StdSetting("设置标题", "这是一个设置",
            AddToggleSwitch(_data, nameof(_data.TestBool))).WarpWithPanel());
        StdAccountPanel accountPanel = new StdAccountPanel("title", "userName", "Description",
            new Button().WarpWithPanel());
        panel.Children.Add(accountPanel);
        return panel;
    }

    private ToggleSwitch AddToggleSwitch(object source, string propName)
    {
        ToggleSwitch toggle = new();
        Binding binding = new()
        {
            Source = source,
            Path = new PropertyPath(propName),
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        toggle.SetBinding(ToggleSwitch.IsOnProperty, binding);
        return toggle;
    }
}