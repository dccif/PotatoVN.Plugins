using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.PluginBase.Controls.Prefabs;
using PotatoVN.App.PluginBase.Helper;
using PotatoVN.App.PluginBase.Models;
using System;
using System.Diagnostics;
using Windows.Storage.Pickers;

namespace PotatoVN.App.PluginBase;

public partial class Plugin
{
    public FrameworkElement CreateSettingUi()
    {
        StdStackPanel root = new();

        // Auto Sync Setting
        ToggleSwitch autoSyncToggle = new()
        {
            IsOn = _data.AutoSync,
            VerticalAlignment = VerticalAlignment.Center
        };
        autoSyncToggle.Toggled += (s, e) =>
        {
            _data.AutoSync = autoSyncToggle.IsOn;
            SaveData();
        };

        var autoSyncSetting = new StdSetting(
            GetLocalized("Ui_AutoSync") ?? "Auto Sync",
            GetLocalized("Ui_AutoSyncDescription") ?? "Automatically sync saves via LAN before game launch.",
            autoSyncToggle);

        root.Children.Add(autoSyncSetting.WarpWithPanel());

        // Header
        TextBlock header = new()
        {
            Text = GetLocalized("Ui_SyncDirectories") ?? "Sync Directories",
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        };
        root.Children.Add(header);

        // List Container
        StackPanel listContainer = new() { Spacing = 8 };

        // Refresh List initially
        RefreshList(listContainer);
        root.Children.Add(listContainer);

        // Add Button
        Button addBtn = new()
        {
            Content = GetLocalized("Ui_Add") ?? "Add",
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        addBtn.Click += (s, e) =>
        {
            _data.SyncDirectories.Add(new SyncDirectory(GetLocalized("Ui_NewFolder") ?? "New Folder", ""));
            SaveData();
            RefreshList(listContainer);
        };
        root.Children.Add(addBtn);

        return root;
    }

    private void RefreshList(StackPanel container)
    {
        container.Children.Clear();

        for (int i = 0; i < _data.SyncDirectories.Count; i++)
        {
            var dir = _data.SyncDirectories[i];
            bool isFixed = i < 2; // First two are fixed (Label/Remove locked)

            Grid row = new();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Label
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); // Path
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Picker
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Remove

            row.ColumnSpacing = 8;
            row.Margin = new Thickness(0, 2, 0, 0);

            // 1. Label
            TextBox labelBox = new()
            {
                Text = dir.Name,
                PlaceholderText = GetLocalized("Ui_Label") ?? "Label",
                IsEnabled = !isFixed // Only editable if not fixed
            };
            // Save on loose focus
            labelBox.LostFocus += (s, e) => { dir.Name = labelBox.Text; SaveData(); };

            // 2. Path (Read-only, set by picker)
            TextBox pathBox = new()
            {
                Text = dir.Path,
                PlaceholderText = GetLocalized("Ui_Path") ?? "Path",
                IsReadOnly = true
            };

            // 3. Browse Button
            Button browseBtn = new()
            {
                Content = "...",
                IsEnabled = true
            };
            browseBtn.Click += async (s, e) =>
            {
                try
                {
                    var picker = new FolderPicker();
                    picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                    picker.FileTypeFilter.Add("*");

                    // Get Window Handle (Hack for WinUI 3 without direct Window reference)
                    var hwnd = Process.GetCurrentProcess().MainWindowHandle;
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                    var folder = await picker.PickSingleFolderAsync();
                    if (folder != null)
                    {
                        dir.Path = folder.Path;
                        pathBox.Text = folder.Path;
                        SaveData();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LanSync] Picker Error: {ex.Message}");
                }
            };

            // 4. Remove Button
            Button removeBtn = new()
            {
                Content = "X", // Or use a symbol icon if available
                IsEnabled = !isFixed
            };
            removeBtn.Click += (s, e) =>
            {
                _data.SyncDirectories.Remove(dir);
                SaveData();
                RefreshList(container);
            };

            Grid.SetColumn(labelBox, 0);
            Grid.SetColumn(pathBox, 1);
            Grid.SetColumn(browseBtn, 2);
            Grid.SetColumn(removeBtn, 3);

            row.Children.Add(labelBox);
            row.Children.Add(pathBox);
            row.Children.Add(browseBtn);
            row.Children.Add(removeBtn);

            container.Children.Add(row);
        }
    }
}