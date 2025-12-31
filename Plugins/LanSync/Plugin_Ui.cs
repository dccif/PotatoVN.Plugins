using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.PluginBase.Controls.Prefabs;
using PotatoVN.App.PluginBase.Helper;
using PotatoVN.App.PluginBase.Models;
using System;
using System.Diagnostics;
using System.Linq;
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
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"]
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
            _data.SyncDirectories.Add(new SyncDirectory(GetLocalized("Ui_GameRoot") ?? "Game Root", "", SyncDirectoryType.Library));
            SaveData();
            RefreshList(listContainer);
        };
        root.Children.Add(addBtn);

        return root;
    }

    private void RefreshList(StackPanel container)
    {
        container.Children.Clear();

        var typeOptions = Enum.GetValues(typeof(SyncDirectoryType))
            .Cast<SyncDirectoryType>()
            .Select(t => new TypeOption(t, GetLocalized($"Ui_Type_{t}") ?? t.ToString()))
            .ToList();

        for (var i = 0; i < _data.SyncDirectories.Count; i++)
        {
            var dir = _data.SyncDirectories[i];
            var isFixed = i < 2; // First two are fixed (Label/Remove/Type locked)

            Grid row = new();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) }); // Type
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Label
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); // Path
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Picker
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Remove

            row.ColumnSpacing = 8;
            row.Margin = new Thickness(0, 4, 0, 4);

            // 0. Type Selector
            ComboBox typeBox = new()
            {
                ItemsSource = typeOptions,
                DisplayMemberPath = "Label",
                SelectedItem = typeOptions.FirstOrDefault(t => t.Type == dir.Type),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = !isFixed
            };
            typeBox.SelectionChanged += (s, e) =>
            {
                if (typeBox.SelectedItem is TypeOption option)
                {
                    dir.Type = option.Type;
                    SaveData();
                }
            };

            // 1. Label
            TextBox labelBox = new()
            {
                Text = dir.Name,
                PlaceholderText = GetLocalized("Ui_Label") ?? "Label",
                IsEnabled = !isFixed // Only editable if not fixed
            };
            // Save on loose focus
            labelBox.LostFocus += (s, e) =>
            {
                dir.Name = labelBox.Text;
                SaveData();
            };

            // 2. Path (Read-only, set by picker)
            TextBox pathBox = new()
            {
                Text = dir.Path,
                PlaceholderText = GetLocalized("Ui_Path") ?? "Path",
                IsReadOnly = true,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 3. Browse Button
            Button browseBtn = new()
            {
                Content = new SymbolIcon(Symbol.Folder),
                IsEnabled = true
            };
            ToolTipService.SetToolTip(browseBtn, GetLocalized("Ui_Browse") ?? "Browse Folder");
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
                Content = new SymbolIcon(Symbol.Delete),
                IsEnabled = !isFixed
            };
            ToolTipService.SetToolTip(removeBtn, GetLocalized("Ui_Remove") ?? "Remove");
            removeBtn.Click += (s, e) =>
            {
                _data.SyncDirectories.Remove(dir);
                SaveData();
                RefreshList(container);
            };

            Grid.SetColumn(typeBox, 0);
            Grid.SetColumn(labelBox, 1);
            Grid.SetColumn(pathBox, 2);
            Grid.SetColumn(browseBtn, 3);
            Grid.SetColumn(removeBtn, 4);

            row.Children.Add(typeBox);
            row.Children.Add(labelBox);
            row.Children.Add(pathBox);
            row.Children.Add(browseBtn);
            row.Children.Add(removeBtn);

            container.Children.Add(row);
        }
    }

    private class TypeOption(SyncDirectoryType type, string label)
    {
        public SyncDirectoryType Type { get; set; } = type;
        public string Label { get; set; } = label;
    }
}