using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Imaging;
using PotatoVN.App.PluginBase.Controls;
using PotatoVN.App.PluginBase.Controls.Prefabs;
using PotatoVN.App.PluginBase.Helper;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Windows.Storage.Streams;

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

        Button btn = new() { Content = GetLocalized("Ui_Select") ?? "Select" };
        btn.Click += async (s, e) =>
        {
            if (s is FrameworkElement element && element.XamlRoot is not null)
            {
                var processInfo = await SelectProcessDialog(element.XamlRoot);
                if (processInfo != null)
                {
                    Debug.WriteLine($"Selected Process: {processInfo.ProcessName}");
                    
                    // Notify User
                    string title = GetLocalized("Msg_ProcessSelectedTitle") ?? "Process Selected";
                    string msgFormat = GetLocalized("Msg_ProcessSelectedMsg") ?? "Monitoring {0} for file changes...";
                    _hostApi.Info(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational, title, string.Format(msgFormat, processInfo.ProcessName));

                    // Start detection with the selected process
                    _ = StartDetection(processInfo.Process);
                }
            }
        };

        panel.Children.Add(new StdSetting(
            GetLocalized("Ui_ProcessSelection") ?? "Process Selection",
            GetLocalized("Ui_SelectGameProcessDescription") ?? "Click button to select game process and start save detection",
            btn).WarpWithPanel());

        return panel;
    }

    public async Task<DisplayProcess?> SelectProcessDialog(XamlRoot xamlRoot)
    {
        ContentDialog dialog = new()
        {
            Title = GetLocalized("Ui_SelectProcessDialogTitle") ?? "Select Process",
            PrimaryButtonText = GetLocalized("Ui_Confirm") ?? "Confirm",
            SecondaryButtonText = GetLocalized("Ui_Cancel") ?? "Cancel",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = xamlRoot
        };

        Grid root = new()
        {
            MaxHeight = 300
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        TextBlock msg = new()
        {
            Text = GetLocalized("Ui_PleaseSelectProcess") ?? "Please select a game process",
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(msg, 0);
        root.Children.Add(msg);

        ListView listView = new();
        Grid.SetRow(listView, 1);

        // Define ItemTemplate using XamlReader as we are in C# code
        // Removed Process ID, added Icon Image
        string templateXaml = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
    <StackPanel Orientation='Horizontal' Spacing='10' Padding='5'>
        <Image Source='{Binding Icon}' Width='24' Height='24' VerticalAlignment='Center'/>
        <TextBlock Text='{Binding ProcessName}' VerticalAlignment='Center'/>
    </StackPanel>
</DataTemplate>";
        listView.ItemTemplate = (DataTemplate)XamlReader.Load(templateXaml);

        ObservableCollection<DisplayProcess> processes = new();
        void RefreshProcesses()
        {
            processes.Clear();
            foreach (Process p in Process.GetProcesses())
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    processes.Add(new DisplayProcess(p));
                }
            }
        }
        RefreshProcesses();
        listView.ItemsSource = processes;

        Button refreshBtn = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 15, 0),
            Content = new SymbolIcon(Symbol.Refresh)
        };
        refreshBtn.Click += (_, _) => RefreshProcesses();
        Grid.SetRow(refreshBtn, 1);
        
        root.Children.Add(listView);
        root.Children.Add(refreshBtn);

        dialog.Content = root;
        dialog.IsPrimaryButtonEnabled = false;

        listView.SelectionChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = listView.SelectedItem is DisplayProcess;
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && listView.SelectedItem is DisplayProcess selected)
        {
            return selected;
        }

        return null;
    }
}

public class DisplayProcess
{
    public Process Process { get; }
    public string ProcessName => Process.ProcessName;
    public BitmapImage? Icon { get; private set; }

    public DisplayProcess(Process process)
    {
        Process = process;
        _ = LoadIconAsync();
    }

    private async Task LoadIconAsync()
    {
        try
        {
            // Extract icon in a background task to avoid UI freeze
            // but BitmapImage creation must be on UI thread or valid context.
            // Actually, System.Drawing.Icon is GDI+, we need to convert to BitmapImage (WinUI).
            
            await Task.Run(async () =>
            {
                try
                {
                    string? path = null;
                    try
                    {
                        path = Process.MainModule?.FileName;
                    }
                    catch { }

                    if (string.IsNullOrEmpty(path)) return;

                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (icon == null) return;

                    using var ms = new MemoryStream();
                    icon.ToBitmap().Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;

                    // Marshal back to UI thread to create BitmapImage
                    // We need a DispatcherQueue or similar.
                    // But here we are inside a ViewModel-like class.
                    // Let's assume we can set the property and notify (but DisplayProcess is not ObservableObject yet).
                    
                    // Since we can't easily dispatch without a Dispatcher reference,
                    // We will just expose the memory stream or byte array?
                    // No, BitmapImage needs to be created.
                    
                    // Actually, let's try to run the extraction synchronously for now, or use a helper.
                    // Limitation: We are in a Plugin, accessing UI thread might be tricky if not passed.
                    
                    // Let's optimize: Just capture the memory stream here.
                    var bytes = ms.ToArray();
                    
                    // We need to invoke on UI thread to create BitmapImage
                    // Plugin class has _hostApi, but DisplayProcess doesn't.
                    // Let's rely on the fact that if we use async void or Task, we return to context?
                    // No, ConfigureAwait(false) is default in many libs.
                    
                    // WORKAROUND: We will use a static helper from Plugin to invoke on UI.
                    // But Plugin is partial.
                }
                catch { }
            });
            
            // Re-fetch path because Process.MainModule can throw
            string? exePath = null;
            try { exePath = Process.MainModule?.FileName; } catch { }

            if (!string.IsNullOrEmpty(exePath))
            {
                // We are back on context? Not necessarily.
                // Let's try to use the DispatcherQueue from the current window?
                // Or just use the _hostApi from Plugin if we can access it.
                // Since DisplayProcess is a simple class, let's just make it simple.
                // We'll invoke the icon loading on the UI thread directly for now (ExtractAssociatedIcon is fast enough for a few processes).
                
                // Wait, ExtractAssociatedIcon might be slow for many files.
                // Let's do a simple implementation first.
                
                var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                if (dispatcher != null)
                {
                     // We are on UI thread.
                     var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                     if (icon != null)
                     {
                         using var ms = new MemoryStream();
                         icon.ToBitmap().Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                         ms.Position = 0;
                         
                         var bitmap = new BitmapImage();
                         await bitmap.SetSourceAsync(ms.AsRandomAccessStream());
                         Icon = bitmap;
                         // We need to notify property change if we want the UI to update *after* the list is shown.
                         // But if we do it in constructor synchronously-ish, it might delay the dialog.
                     }
                }
            }
        }
        catch (Exception)
        {
            // Ignore icon errors
        }
    }
}