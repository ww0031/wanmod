using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Games;

namespace UEManager.UI.ViewModels;

/// <summary>A game's card in the game library: its cover, its name, and what a right-click offers.</summary>
public sealed partial class GameCardViewModel(MainViewModel owner, LibraryGame entry) : LocalizedObservableObject
{
    public LibraryGame Entry => entry;

    public string Name => owner.NameOf(entry);

    /// <summary>The first letter of the name, on the card of a game without a cover.</summary>
    public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";

    public string KindText => entry.Unreal ? L.T("Unreal Engine") : L.T("Generic mode");

    public string StoreText => entry.Store switch
    {
        GameStore.Steam => "Steam",
        GameStore.Epic => "Epic",
        _ => L.T("Added by hand"),
    };

    public bool IsCurrent => owner.CurrentGame == entry;

    public bool HasCustomCover => entry.CoverFile is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private ImageSource? cover;

    public bool HasCover => Cover is not null;

    [RelayCommand]
    private Task Open() => owner.OpenGameAsync(entry);

    [RelayCommand]
    private Task Rename() => owner.RenameGameAsync(this);

    [RelayCommand]
    private Task ChangeCover() => owner.ChangeCoverAsync(this);

    [RelayCommand]
    private Task ResetCover() => owner.ResetCoverAsync(this);

    [RelayCommand]
    private void OpenFolder() => owner.OpenGameFolder(entry);

    [RelayCommand]
    private Task Remove() => owner.RemoveGameAsync(entry);
}
