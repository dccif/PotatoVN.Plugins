using CommunityToolkit.Mvvm.ComponentModel;

namespace PotatoVN.App.PluginBase.Models;

public partial class SyncDirectory : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _path = string.Empty;

    public SyncDirectory() { }

    public SyncDirectory(string name, string path)
    {
        Name = name;
        Path = path;
    }
}
