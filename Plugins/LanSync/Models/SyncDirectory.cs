using CommunityToolkit.Mvvm.ComponentModel;

namespace PotatoVN.App.PluginBase.Models;

public enum SyncDirectoryType
{
    User,
    Library
}

public partial class SyncDirectory : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _path = string.Empty;
    [ObservableProperty] private SyncDirectoryType _type = SyncDirectoryType.Library;
    [ObservableProperty] private bool _isCustomName = false;

    public SyncDirectory()
    {
    }

    public SyncDirectory(string name, string path, SyncDirectoryType type = SyncDirectoryType.Library)
    {
        Name = name;
        Path = path;
        Type = type;
    }
}