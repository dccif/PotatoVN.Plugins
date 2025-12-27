using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.PluginBase.Controls.Prefabs;
using PotatoVN.App.PluginBase.Helper;


namespace PotatoVN.App.PluginBase;

public partial class Plugin
{
    public FrameworkElement CreateSettingUi()
    {
        StdStackPanel stdStackPanel = new();
        StdStackPanel stdSetting = new();

        ToggleSwitch adminToggle = new()
        {
            IsOn = _data.UseAdminMode,
            VerticalAlignment = VerticalAlignment.Center
        };

        StdSetting adminModeSetting = new StdSetting(
            GetLocalized("Ui_UseAdminMode") ?? "Use Admin Mode (ETW)",
            GetLocalized("Ui_UseAdminModeDescription") ?? "In most cases, the standard watcher mode is sufficient. Only enable this if you know what you are doing.",
            adminToggle);
        stdSetting.Children.Add(adminModeSetting);

        if (!IsAdministrator())
        {
            Button restartBtn = new()
            {
                Content = GetLocalized("Ui_RestartAsAdmin") ?? "Restart as Admin",
                VerticalAlignment = VerticalAlignment.Center
            };
            restartBtn.Click += (s, e) => RestartAsAdmin();

            StdSetting restartSetting = new StdSetting(
                GetLocalized("Ui_AdminRequiredWarning") ?? "Administrator privileges required.",
                GetLocalized("Ui_AdminRequiredDescription") ?? "Restart the application as administrator to enable ETW detection.",
                restartBtn);

            restartSetting.Visibility = _data.UseAdminMode ? Visibility.Visible : Visibility.Collapsed;
            stdSetting.Children.Add(restartSetting);

            adminToggle.Toggled += (s, e) =>
            {
                _data.UseAdminMode = adminToggle.IsOn;
                restartSetting.Visibility = adminToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            };
        }
        else
        {
            adminToggle.Toggled += (s, e) => _data.UseAdminMode = adminToggle.IsOn;
        }

        stdStackPanel.Children.Add(stdSetting.WarpWithPanel());

        return stdStackPanel;
    }
}
