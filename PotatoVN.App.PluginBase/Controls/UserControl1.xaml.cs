using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace PotatoVN.App.PluginBase.Controls
{
    public sealed partial class UserControl1 : UserControl
    {
        public UserControl1()
        {
            PluginInitializeComponent();
        }

        private void PluginInitializeComponent()
        {
            if (_contentLoaded)
                return;

            _contentLoaded = true;

            var resourceLocator = XamlResourceLocatorFactory.Create();
            Application.LoadComponent(this, resourceLocator, ComponentResourceLocation.Application);
        }

        private void PluginButton_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "插件按钮在 " + System.DateTime.Now.ToString(CultureInfo.InvariantCulture) + " 被点击了！";
        }
    }
}
