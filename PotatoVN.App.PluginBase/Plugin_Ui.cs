using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.PluginBase.Controls.Prefabs;
using PotatoVN.App.PluginBase.Helper;


namespace PotatoVN.App.PluginBase;

public partial class Plugin
{
    public FrameworkElement CreateSettingUi()
    {
        StdStackPanel panel = new();

        ToggleSwitch adminToggle = new() { IsOn = _data.UseAdminMode };
        
        StackPanel adminContainer = new();
        adminContainer.Children.Add(adminToggle);

        if (!IsAdministrator())
        {
            Button restartBtn = new()
            {
                Content = GetLocalized("Ui_RestartAsAdmin") ?? "Restart as Admin",
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = _data.UseAdminMode ? Visibility.Visible : Visibility.Collapsed
            };
            restartBtn.Click += (s, e) => RestartAsAdmin();

            TextBlock warning = new()
            {
                Text = GetLocalized("Ui_AdminRequiredWarning") ?? "Administrator privileges required.",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 185, 0)),
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0),
                Visibility = _data.UseAdminMode ? Visibility.Visible : Visibility.Collapsed
            };

            adminToggle.Toggled += (s, e) =>
            {
                _data.UseAdminMode = adminToggle.IsOn;
                restartBtn.Visibility = adminToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
                warning.Visibility = adminToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            };

            adminContainer.Children.Add(warning);
            adminContainer.Children.Add(restartBtn);
        }
        else
        {
            adminToggle.Toggled += (s, e) => _data.UseAdminMode = adminToggle.IsOn;
        }

        panel.Children.Add(new StdSetting(
            GetLocalized("Ui_UseAdminMode") ?? "Use Admin Mode (ETW)",
            GetLocalized("Ui_UseAdminModeDescription") ?? "Uses Windows Event Tracing for more accurate real-time detection.",
            adminContainer).WarpWithPanel());

        return panel;
    }
}
