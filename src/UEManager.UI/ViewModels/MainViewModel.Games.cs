using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using UEManager.Core;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Stores;
using UEManager.UI.Catalog;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>
/// The game library, the app's first page: the games found through Steam and Epic and those the player added, each
/// with its cover. Opening one connects the other pages to it.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The game the app's own artwork belongs to.</summary>
    private const string ArtGameId = "phantom-blade-zero";

    private GameLibrary library = new();

    /// <summary>The library was read at this start; before that, no game is chosen yet.</summary>
    private bool hasLibrary;

    private ICollectionView? gamesView;
    private RemoteImages? coverImages;

    /// <summary>The Steam app whose pictures a game without its own shows, by the game's id, searched once per start.</summary>
    private readonly Dictionary<string, Task<string?>> artSearches = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The wide picture of the opened game, behind the pages; null for none.</summary>
    private ImageSource? hero;

    public ObservableCollection<GameCardViewModel> GameCards { get; } = [];

    public ICollectionView GamesView => gamesView ??= CreateGamesView();

    /// <summary>The game the other pages work on; null before one is opened.</summary>
    public LibraryGame? CurrentGame { get; private set; }

    [ObservableProperty]
    private string gameSearch = "";

    partial void OnGameSearchChanged(string value)
    {
        GamesView.Refresh();
        OnPropertyChanged(nameof(GamesCountText));
    }

    public IReadOnlyList<TagFilterItem> GameSortModes =>
    [
        new(L.T("Recently played"), !settings.GamesByName, new RelayCommand(() => SetGamesByName(false))),
        new(L.T("Name"), settings.GamesByName, new RelayCommand(() => SetGamesByName(true))),
    ];

    public string GamesCountText => L.Plural(GamesView.Cast<object>().Count(), "{0} game", "{0} games");

    public bool HasNoGames => hasLibrary && GameCards.Count == 0;

    /// <summary>Unreal games get pak and UE4SS mods, and the Loaders page; a game in generic mode does not.</summary>
    public bool IsUnrealGame => !Profile.Generic;

    /// <summary>In generic mode, where mods go below the game folder.</summary>
    public string ModsFolderText => CurrentGame is { ModsFolder: { Length: > 0 } folder } ? folder : L.T("The game folder itself");

    public ImageSource? Background => hero;

    public bool HasBackground => Background is not null;

    /// <summary>A game's name as the library shows it: the player's own, else the store's, a built-in game's in the app's language.</summary>
    internal string NameOf(LibraryGame game) =>
        game.CustomName ?? (GameLibrary.BuiltIn.Any(p => p.Id == game.Id) ? L.T(game.Name) : game.Name);

    private ICollectionView CreateGamesView()
    {
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(GameCards);
        view.Filter = item => item is GameCardViewModel card
            && (GameSearch.Trim() is not { Length: > 0 } search
                || card.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || card.Entry.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        view.CustomSort = Comparer<object>.Create((a, b) => CompareGames((GameCardViewModel)a, (GameCardViewModel)b));
        return view;
    }

    private int CompareGames(GameCardViewModel a, GameCardViewModel b)
    {
        var byName = StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name);
        return settings.GamesByName || a.Entry.LastUsed == b.Entry.LastUsed ? byName : b.Entry.LastUsed.CompareTo(a.Entry.LastUsed);
    }

    private void SetGamesByName(bool byName)
    {
        settings.GamesByName = byName;
        SaveSettings();
        GamesView.Refresh();
        OnPropertyChanged(nameof(GameSortModes));
    }

    /// <summary>Builds the cards again from the library, and loads their covers.</summary>
    private void RefreshGameCards()
    {
        GameCards.Clear();
        foreach (var game in library.Games)
        {
            var card = new GameCardViewModel(this, game);
            GameCards.Add(card);
            _ = LoadCoverAsync(card);
        }
        GamesView.Refresh();
        OnPropertyChanged(nameof(GamesCountText));
        OnPropertyChanged(nameof(HasNoGames));
    }

    /// <summary>
    /// The player's picture, else Steam's cover on this PC, else Steam's from the store; a game with none of these takes
    /// the cover of the game with its name in the Steam store. Without any, the card shows the name's first letter.
    /// </summary>
    private async Task LoadCoverAsync(GameCardViewModel card)
    {
        var game = card.Entry;
        if (game.CoverFile is { } own && await LoadLocalImageAsync(Path.Combine(paths.CoversDir, own), 360) is { } chosen)
        {
            card.Cover = chosen;
            return;
        }
        if (game.SteamAppId is { Length: > 0 } appId)
        {
            var art = await Task.Run(() => SteamLibrary.LibraryArt(appId));
            card.Cover = File.Exists(art.Cover) ? await LoadLocalImageAsync(art.Cover, 360) : await LoadRemoteImageAsync(art.Cover, 360);
            if (card.Cover is not null)
                return;
        }
        if (await FindArtAppIdAsync(game) is { } artId)
            card.Cover = await LoadRemoteImageAsync(SteamStore.CoverUrl(artId), 360);
    }

    private Task<ImageSource?> LoadRemoteImageAsync(string url, int width) => (coverImages ??= new RemoteImages(Http)).LoadAsync(url, width);

    /// <summary>The Steam app whose pictures a game without its own shows: the one found before, else the one the store has under its name.</summary>
    private Task<string?> FindArtAppIdAsync(LibraryGame game)
    {
        if (game.ArtAppId is { Length: > 0 } || game.ArtChecked)
            return Task.FromResult(game.ArtAppId);
        if (!artSearches.TryGetValue(game.Id, out var search))
            artSearches[game.Id] = search = SearchArtAppIdAsync(game);
        return search;
    }

    private async Task<string?> SearchArtAppIdAsync(LibraryGame game)
    {
        try
        {
            var appId = await SteamStore.FindAppIdAsync(Http, game.CustomName is { } custom ? [game.Name, custom] : [game.Name], game.SteamAppId);
            game.ArtAppId = appId;
            game.ArtChecked = appId is null;
            library.TrySave(paths);
            Log.Info(appId is null ? $"The Steam store has no game named \"{game.Name}\" for its pictures." : $"\"{game.Name}\" shows the pictures of Steam app {appId}.");
            return appId;
        }
        catch (ModException ex)
        {
            Log.Warn($"The Steam store could not be searched for \"{game.Name}\": {ex.Message}");
            return null;
        }
    }

    private static Task<ImageSource?> LoadLocalImageAsync(string file, int width) => Task.Run<ImageSource?>(() =>
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = width;
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            Log.Warn($"Picture '{file}' could not be loaded: {ex.Message}");
            return null;
        }
    });

    /// <summary>The game opened last, else Phantom Blade Zero, else the only game; null for none.</summary>
    private LibraryGame? LastGame() =>
        library.Games.Where(g => g.LastOpened is not null).MaxBy(g => g.LastOpened)
        ?? library.Find(ArtGameId)
        ?? (library.Games is [var only] ? only : null);

    /// <summary>Reads the library at start, opens the game the player had open last, and looks for new games meanwhile.</summary>
    private async Task LoadLibraryAsync()
    {
        await RunAsync(L.T("Looking for the game…"), async () => library = await Task.Run(() => GameLibrary.Load(paths, settings)));
        hasLibrary = true;
        RefreshGameCards();
        if (LastGame() is { } last)
            await OpenGameAsync(last, show: false);
        else
            await SetGameAsync(null);
        SelectedPage = "Games";
        _ = ScanGamesAsync(quiet: true);
    }

    [RelayCommand]
    private Task ScanGames() => ScanGamesAsync(quiet: false);

    /// <summary>Adds the games Steam and Epic have that the library lacks. At start it runs quietly behind the page.</summary>
    private async Task ScanGamesAsync(bool quiet)
    {
        IReadOnlyList<FoundGame> found = [];
        if (quiet)
        {
            try
            {
                found = await Task.Run(() => GameScanner.Scan());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"The games on this PC could not be listed: {ex.Message}");
                return;
            }
        }
        else if (!await RunAsync(L.T("Searching Steam and Epic for games…"), async () => found = await Task.Run(() => GameScanner.Scan())))
            return;
        var added = library.Merge(found, DateTimeOffset.Now);
        library.TrySave(paths);
        RefreshGameCards();
        Log.Info($"Found {found.Count} game(s) through Steam and Epic; {added.Count} new.");
        if (!quiet)
            ShowToast(added.Count == 0 ? L.T("No new games were found.") : L.Plural(added.Count, "Added {0} game.", "Added {0} games."));
    }

    [RelayCommand]
    private async Task AddGame()
    {
        var dialog = new OpenFolderDialog { Title = L.T("Choose the game's install folder") };
        if (dialog.ShowDialog() != true)
            return;
        FoundGame? found = null;
        if (!await RunAsync(L.T("Reading the folder…"), async () => found = await Task.Run(() => GameScanner.FromFolder(dialog.FolderName))))
            return;
        if (found is null)
        {
            ShowToast(L.T("This folder holds no game: there is no .exe file in it. Choose the game's main folder."), isError: true);
            return;
        }
        var entry = library.Add(found, DateTimeOffset.Now);
        library.TrySave(paths);
        RefreshGameCards();
        await OpenGameAsync(entry);
    }

    /// <summary>Connects the pages to a game of the library. Downloads in progress keep the game they are for.</summary>
    /// <param name="show">Goes to the game's Home page once it is open.</param>
    internal async Task OpenGameAsync(LibraryGame entry, bool show = true)
    {
        if (entry == CurrentGame && manager is not null)
        {
            if (show)
                SelectedPage = "Home";
            return;
        }
        if (Downloads.Count > 0)
        {
            ShowToast(L.T("Wait until the downloads are done, then switch the game."), isError: true);
            return;
        }
        GameInstall? install = null;
        await RunAsync(L.T("Looking for the game…"), async () => install = await Task.Run(entry.Locate));
        SwitchTo(entry);
        await SetGameAsync(install);
        if (Game is { } game)
            entry.RootPath = game.RootPath;
        else
            ShowToast(L.F($"{GameDisplayName} is no longer in {entry.RootPath}. Choose its folder in Settings."), isError: true);
        entry.LastOpened = DateTimeOffset.Now;
        library.TrySave(paths);
        RefreshGameCards();
        if (show)
            SelectedPage = "Home";
        _ = CheckUe4ssManifestAsync();
        _ = TranslateCardsAsync();
        _ = CheckNexusUpdatesAsync(quiet: true);
        _ = FindNexusDomainAsync(entry);
    }

    /// <summary>Forgets what belonged to the game open before: its profile, its Nexus Mods page, its offers and its picture.</summary>
    private void SwitchTo(LibraryGame? entry)
    {
        CurrentGame = entry;
        if (entry is not null)
            Profile = entry.ToProfile();
        browse = null;
        nexusUpdates.Clear();
        isRecommendedKnown = false;
        RecommendedMain.Offer(null);
        RecommendedExtra.Offer(null);
        latestUe4ss = null;
        hero = null;
        if (entry is not null)
            _ = LoadHeroAsync(entry);
    }

    /// <summary>The game's profile again, after its Nexus Mods name or its mods folder changed.</summary>
    private void RefreshProfile(LibraryGame entry)
    {
        library.TrySave(paths);
        if (entry != CurrentGame)
            return;
        Profile = entry.ToProfile();
        browse = null;
        OnPropertyChanged(string.Empty);
    }

    private async Task LoadHeroAsync(LibraryGame entry)
    {
        if (await LoadHeroImageAsync(entry) is { } picture && CurrentGame == entry)
            hero = picture;
        OnPropertyChanged(nameof(Background));
        OnPropertyChanged(nameof(HasBackground));
    }

    /// <summary>Steam's wide picture of the game on this PC, else from the store, else that of the game with its name in the store.</summary>
    private async Task<ImageSource?> LoadHeroImageAsync(LibraryGame entry)
    {
        if (entry.SteamAppId is { Length: > 0 } appId)
        {
            if ((await Task.Run(() => SteamLibrary.LibraryArt(appId))).Hero is { } file && await LoadLocalImageAsync(file, 1600) is { } local)
                return local;
            if (await LoadRemoteImageAsync(SteamStore.HeroUrl(appId), 1600) is { } remote)
                return remote;
        }
        return await FindArtAppIdAsync(entry) is { } artId ? await LoadRemoteImageAsync(SteamStore.HeroUrl(artId), 1600) : null;
    }

    /// <summary>Asks Nexus Mods once for the game's short name there, which its Browse page and links need.</summary>
    private async Task FindNexusDomainAsync(LibraryGame entry)
    {
        if (entry.NexusDomain is not null || entry.NexusChecked)
            return;
        try
        {
            var domain = await NexusMods.FindGameDomainAsync(Http, entry.Name);
            entry.NexusDomain = domain;
            entry.NexusChecked = domain is null;
            Log.Info(domain is null ? $"Nexus Mods has no game named \"{entry.Name}\"." : $"\"{entry.Name}\" is \"{domain}\" on Nexus Mods.");
            RefreshProfile(entry);
        }
        catch (ModException ex)
        {
            Log.Warn($"Nexus Mods could not be asked for \"{entry.Name}\": {ex.Message}");
        }
    }

    /// <summary>
    /// Makes sure a Nexus Mods mod is for the open game: a game that has no Nexus Mods name yet takes the mod's, and a
    /// mod for another game of the library opens that game.
    /// </summary>
    private async Task<bool> EnsureNexusGameAsync(NexusModRef mod)
    {
        if (NexusGame is { } game && string.Equals(mod.Game, game, StringComparison.OrdinalIgnoreCase))
            return true;
        if (library.Games.FirstOrDefault(g => string.Equals(g.NexusDomain, mod.Game, StringComparison.OrdinalIgnoreCase)) is { } other)
        {
            await OpenGameAsync(other, show: false);
            return CurrentGame == other && manager is not null;
        }
        if (NexusGame is null && CurrentGame is { } current)
        {
            current.NexusDomain = mod.Game.ToLowerInvariant();
            current.NexusChecked = false;
            RefreshProfile(current);
            return true;
        }
        ShowToast(L.F($"This link is for another game on Nexus Mods ({mod.Game}), not {GameDisplayName}."), isError: true);
        return false;
    }

    internal async Task RenameGameAsync(GameCardViewModel card)
    {
        if (await AskTextAsync(L.T("Rename game"), L.T("The name the library shows. Leave it empty for the store's name."), L.T("Rename"),
                card.Name) is not { } name)
            return;
        card.Entry.CustomName = name.Trim() is { Length: > 0 } custom && custom != card.Entry.Name ? custom : null;
        library.TrySave(paths);
        RefreshGameCards();
        OnPropertyChanged(string.Empty);
    }

    internal async Task ChangeCoverAsync(GameCardViewModel card)
    {
        var dialog = new OpenFileDialog { Title = L.T("Choose a cover picture"), Filter = $"{L.T("Pictures")}|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
        if (dialog.ShowDialog() != true)
            return;
        var file = card.Entry.Id + Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (!await RunAsync(L.T("Saving the cover…"), () => Task.Run(() =>
            {
                Directory.CreateDirectory(paths.CoversDir);
                if (card.Entry.CoverFile is { } old && old != file)
                    DeleteCover(old);
                File.Copy(dialog.FileName, Path.Combine(paths.CoversDir, file), overwrite: true);
            })))
            return;
        card.Entry.CoverFile = file;
        library.TrySave(paths);
        RefreshGameCards();
    }

    internal Task ResetCoverAsync(GameCardViewModel card)
    {
        if (card.Entry.CoverFile is { } old)
            DeleteCover(old);
        card.Entry.CoverFile = null;
        library.TrySave(paths);
        RefreshGameCards();
        return Task.CompletedTask;
    }

    private void DeleteCover(string file)
    {
        try
        {
            File.Delete(Path.Combine(paths.CoversDir, file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"The old cover '{file}' could not be deleted: {ex.Message}");
        }
    }

    internal void OpenGameFolder(LibraryGame game)
    {
        if (Directory.Exists(game.RootPath))
            Shell.Open(game.RootPath);
        else
            ShowToast(L.F($"{NameOf(game)} is no longer in {game.RootPath}."), isError: true);
    }

    internal async Task RemoveGameAsync(LibraryGame game)
    {
        if (!await AskAsync(L.T("Remove from the library"),
                L.F($"Remove \"{NameOf(game)}\" from the game library? Its mods stay in the game, and the manager keeps their list: add the game again to see them."),
                L.T("Remove")))
            return;
        if (game == CurrentGame)
        {
            if (Downloads.Count > 0)
            {
                ShowToast(L.T("Wait until the downloads are done, then switch the game."), isError: true);
                return;
            }
            SwitchTo(null);
            await SetGameAsync(null);
        }
        library.Remove(game);
        library.TrySave(paths);
        RefreshGameCards();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Generic mode: the folder below the game folder that downloads go into.</summary>
    [RelayCommand]
    private async Task ChooseModsFolder()
    {
        if (CurrentGame is not { } entry || Game is null)
            return;
        var dialog = new OpenFolderDialog { Title = L.T("Choose the folder mods go into"), InitialDirectory = entry.RootPath };
        if (dialog.ShowDialog() != true)
            return;
        var relative = Path.GetRelativePath(entry.RootPath, dialog.FolderName);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            ShowToast(L.F($"Choose a folder inside the game folder, {entry.RootPath}."), isError: true);
            return;
        }
        entry.ModsFolder = relative == "." ? "" : relative.Replace('\\', '/');
        RefreshProfile(entry);
        await SetGameAsync(entry.Locate());
    }
}
