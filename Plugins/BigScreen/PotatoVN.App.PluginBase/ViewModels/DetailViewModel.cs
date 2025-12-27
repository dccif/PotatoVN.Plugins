using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GalgameManager.Models;
using PotatoVN.App.PluginBase.Messages;

namespace PotatoVN.App.PluginBase.ViewModels;

public partial class DetailViewModel : ObservableObject
{
    [ObservableProperty]
    private Galgame _game;

    public DetailViewModel(Galgame game)
    {
        _game = game;
    }

    [RelayCommand]
    private void Play()
    {
        WeakReferenceMessenger.Default.Send(new PlayGameMessage(Game));
    }

    [RelayCommand]
    private void Back()
    {
        WeakReferenceMessenger.Default.Send(new BigScreenCloseOverlayMessage());
    }
}
