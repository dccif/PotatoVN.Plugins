using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.PluginBase.Controls.Prefabs;

namespace PotatoVN.App.PluginBase.Helper;

public static class UserControlExtension
{
    /// <summary>
    /// 使用Panel包裹某个控件
    /// </summary>
    /// <param name="control"></param>
    /// <returns>被包裹后的控件</returns>
    public static UserControl WarpWithPanel(this UIElement control)
    {
        return new StdPanel(control);
    }
}