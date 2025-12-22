using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GalgameManager.Models;
using PotatoVN.App.PluginBase.Messages;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PotatoVN.App.PluginBase.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Galgame> _recentGames = new();

    [ObservableProperty]
    private ObservableCollection<Galgame> _libraryGames = new();

    public HomeViewModel(List<Galgame> games)
    {
        // 1. Prepare Recent Games (Always LastPlayTime Descending)
        var recentList = new List<Galgame>(games);
        recentList.Sort((a, b) => b.LastPlayTime.CompareTo(a.LastPlayTime));
        // Take top 10 for the horizontal scroll
        var topRecent = new List<Galgame>();
        int count = 0;
        foreach(var g in recentList)
        {
             if(count >= 10) break;
             topRecent.Add(g);
             count++;
        }
        RecentGames = new ObservableCollection<Galgame>(topRecent);

        // 2. Prepare Library Games (User Preference)
        SortGames(games);
        LibraryGames = new ObservableCollection<Galgame>(games);
    }

    private void SortGames(List<Galgame> games)
    {
        var sortType = Plugin.CurrentData.SortType;
        var ascending = Plugin.CurrentData.SortAscending;

        if (sortType == Models.SortType.LastPlayTime)
        {
            if (ascending) games.Sort((a, b) => a.LastPlayTime.CompareTo(b.LastPlayTime));
            else games.Sort((a, b) => b.LastPlayTime.CompareTo(a.LastPlayTime));
        }
        else // AddTime
        {
            if (ascending) games.Sort((a, b) => a.AddTime.CompareTo(b.AddTime));
            else games.Sort((a, b) => b.AddTime.CompareTo(a.AddTime));
        }
    }

    [RelayCommand]
    private void ItemClick(Galgame? game)
    {
        if (game != null)
        {
            WeakReferenceMessenger.Default.Send(new NavigateToDetailMessage(game));
        }
    }
}
