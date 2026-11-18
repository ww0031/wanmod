using System.Security.Cryptography;
using System.Text;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.Core.Stores;

namespace UEManager.Core.Games;

/// <summary>A game in the game library, as library.json keeps it.</summary>
public sealed class LibraryGame
{
    /// <summary>
    /// The game's key: the folder of its mod list below Data\games, and its entries in the settings. A built-in
    /// profile's id, as "phantom-blade-zero", else "steam-&lt;app id&gt;", "epic-&lt;app name&gt;" or "folder-&lt;hash&gt;".
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>The name the store, the folder or the built-in profile gives the game.</summary>
    public string Name { get; set; } = "";

    /// <summary>The name the player gave the game; null shows <see cref="Name"/>.</summary>
    public string? CustomName { get; set; }

    public string RootPath { get; set; } = "";

    public GameStore Store { get; set; }

    public string? SteamAppId { get; set; }

    /// <summary>Made with Unreal Engine: pak and UE4SS mods. Otherwise the game is managed in generic mode.</summary>
    public bool Unreal { get; set; }

    /// <summary>The Unreal project folder, the one holding Content\Paks; null when not known or not an Unreal game.</summary>
    public string? ProjectFolder { get; set; }

    /// <summary>In generic mode, the folder mods go into, below the install folder; empty for the install folder itself.</summary>
    public string ModsFolder { get; set; } = "";

    /// <summary>The game's short name on Nexus Mods, such as "phantombladezero"; null while not known.</summary>
    public string? NexusDomain { get; set; }

    /// <summary>Nexus Mods was asked for the game's short name and had none that matches; it is not asked again by itself.</summary>
    public bool NexusChecked { get; set; }

    /// <summary>The picture the player chose for the game, a file in Data\covers; null for the store's.</summary>
    public string? CoverFile { get; set; }

    /// <summary>The Steam app whose pictures the game shows when it has none of its own, found by its name in the store.</summary>
    public string? ArtAppId { get; set; }

    /// <summary>The Steam store was searched for the game's pictures and had none; it is not searched again by itself.</summary>
    public bool ArtChecked { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    /// <summary>When the player last opened the game in the manager.</summary>
    public DateTimeOffset? LastOpened { get; set; }

    /// <summary>When the manager last started the game.</summary>
    public DateTimeOffset? LastLaunched { get; set; }

    /// <summary>When Steam last ran the game, as of the last scan; null when unknown.</summary>
    public DateTimeOffset? LastPlayed { get; set; }

    /// <summary>The last time the game was played or opened here: the library lists the games by it.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset LastUsed => new[] { LastPlayed, LastLaunched, LastOpened }.Max() ?? DateTimeOffset.MinValue;

    /// <summary>The built-in profile of the game, else one with the rules every Unreal game shares, or with generic mode.</summary>
    public GameProfile ToProfile()
    {
        if (GameLibrary.BuiltIn.FirstOrDefault(p => p.Id == Id) is { } known)
            return known;
        var page = NexusDomain is { Length: > 0 } domain ? $"https://www.nexusmods.com/{domain}/mods/{{id}}" : null;
        return Unreal
            ? new GameProfile { Id = Id, DisplayName = Name, SteamAppId = SteamAppId, ProjectFolder = ProjectFolder, ModPageUrl = page }
            : new GameProfile
            {
                Id = Id, DisplayName = Name, SteamAppId = SteamAppId, Generic = true, ModPageUrl = page,
                PaksPath = ModsFolder, ModsPath = ModsFolder, LogicModsPath = ModsFolder, BinariesPath = "",
            };
    }

    /// <summary>The game's install as it is now; null when its folder is gone or no longer holds the game.</summary>
    public GameInstall? Locate()
    {
        var profile = ToProfile();
        if (GameLibrary.BuiltIn.Contains(profile))
            return GameLocator.FromFolder(profile, RootPath) ?? GameLocator.Discover(profile);
        if (!Directory.Exists(RootPath))
            return null;
        if (SteamAppId is { Length: > 0 } appId && SteamLibrary.FindAppInLibraryOf(RootPath, appId) is { } steam
            && FileSystem.IsSameFolder(steam.InstallPath, RootPath))
            return GameLocator.Resolve(profile, RootPath, GameStore.Steam, steam.BuildId, SteamLibrary.LaunchUri(appId));
        if (EpicLauncher.FindAppIn(RootPath) is { } epic)
            return GameLocator.Resolve(profile, RootPath, GameStore.Epic, epic.Version, epic.LaunchUri);
        return GameLocator.Resolve(profile, RootPath, GameStore.Manual);
    }

    /// <summary>The library entry of a game a scan or a picked folder found.</summary>
    public static LibraryGame For(FoundGame found, DateTimeOffset now)
    {
        var known = GameLibrary.BuiltIn.FirstOrDefault(p =>
            (found.SteamAppId is not null && p.SteamAppId == found.SteamAppId)
            || (found.Store == GameStore.Epic && string.Equals(p.EpicDisplayName, found.Name, StringComparison.OrdinalIgnoreCase))
            || (found.ProjectFolder is not null && string.Equals(p.ProjectFolder, found.ProjectFolder, StringComparison.OrdinalIgnoreCase)));
        var id = known?.Id ?? found switch
        {
            { SteamAppId: { Length: > 0 } appId } => $"steam-{appId}",
            { EpicAppName: { Length: > 0 } app } when app.All(char.IsAsciiLetterOrDigit) => $"epic-{app.ToLowerInvariant()}",
            { Store: GameStore.Epic } => $"epic-{Hash(found.RootPath)}",
            _ => $"folder-{Hash(found.RootPath)}",
        };
        return new LibraryGame
        {
            Id = id,
            Name = known?.DisplayName ?? found.Name,
            RootPath = found.RootPath,
            Store = found.Store,
            SteamAppId = found.SteamAppId ?? known?.SteamAppId,
            Unreal = known is not null || found.Unreal,
            ProjectFolder = found.ProjectFolder ?? known?.ProjectFolder,
            NexusDomain = NexusMods.GameOf(known?.ModPageUrl),
            LastPlayed = found.LastPlayed,
            AddedAt = now,
        };
    }

    /// <summary>The library entry of a built-in game found where the player had it before there was a library.</summary>
    internal static LibraryGame For(GameProfile profile, GameInstall install, DateTimeOffset now) => new()
    {
        Id = profile.Id,
        Name = profile.DisplayName,
        RootPath = install.RootPath,
        Store = install.Store,
        SteamAppId = profile.SteamAppId,
        Unreal = true,
        ProjectFolder = install.ProjectFolder,
        NexusDomain = NexusMods.GameOf(profile.ModPageUrl),
        AddedAt = now,
    };

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).TrimEnd('\\', '/').ToUpperInvariant())), 0, 6)
            .ToLowerInvariant();
}

/// <summary>The games the player manages mods for: library.json in the data folder.</summary>
public sealed class GameLibrary
{
    private static readonly Lazy<IReadOnlyList<GameProfile>> Profiles = new(GameProfile.BuiltIn);

    /// <summary>The games with a profile of their own, built into the app.</summary>
    public static IReadOnlyList<GameProfile> BuiltIn => Profiles.Value;

    public List<LibraryGame> Games { get; set; } = [];

    /// <summary>The ids of the games the player removed from the library: a scan does not add them again.</summary>
    public List<string> Removed { get; set; } = [];

    /// <summary>
    /// The library as saved. At the first start with a library, the built-in games found on this PC go into it, each
    /// where the player chose it before, so their mods stay as they were.
    /// </summary>
    public static GameLibrary Load(AppPaths paths, AppSettings settings, Func<GameProfile, GameInstall?>? discover = null)
    {
        try
        {
            if (JsonFile.Read<GameLibrary>(paths.LibraryFile) is { } saved)
                return saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.Warn($"The game library could not be read, so it starts again from the games found: {ex.Message}");
        }
        discover ??= GameLocator.Discover;
        var library = new GameLibrary();
        foreach (var profile in BuiltIn)
        {
            var install = settings.GamePaths.TryGetValue(profile.Id, out var chosen) ? GameLocator.FromFolder(profile, chosen) : null;
            install ??= discover(profile);
            if (install is not null)
                library.Games.Add(LibraryGame.For(profile, install, DateTimeOffset.Now));
        }
        Log.Info($"Started the game library with {library.Games.Count} game(s) found on this PC.");
        library.TrySave(paths);
        return library;
    }

    public LibraryGame? Find(string id) => Games.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The entry of a game in this folder, or of the same store game, if the library has one.</summary>
    public LibraryGame? Find(FoundGame found) =>
        Games.FirstOrDefault(g => FileSystem.IsSameFolder(g.RootPath, found.RootPath)
            || (found.SteamAppId is not null && g.SteamAppId == found.SteamAppId));

    /// <summary>
    /// Takes in what a scan found: a game the library lacks is added, unless the player removed it; a game it has
    /// gets the time Steam last ran it, and its new folder when it moved.
    /// </summary>
    /// <returns>The games added.</returns>
    public IReadOnlyList<LibraryGame> Merge(IEnumerable<FoundGame> found, DateTimeOffset now)
    {
        var added = new List<LibraryGame>();
        foreach (var game in found)
        {
            if (Find(game) is { } known)
            {
                known.LastPlayed = game.LastPlayed ?? known.LastPlayed;
                if (!Directory.Exists(known.RootPath))
                    known.RootPath = game.RootPath;
                continue;
            }
            var entry = LibraryGame.For(game, now);
            if (Removed.Contains(entry.Id, StringComparer.OrdinalIgnoreCase) || Find(entry.Id) is not null)
                continue;
            Games.Add(entry);
            added.Add(entry);
        }
        return added;
    }

    /// <summary>Adds a game the player picked, even one removed before; a game the library has already stays as it is.</summary>
    public LibraryGame Add(FoundGame found, DateTimeOffset now)
    {
        if (Find(found) is { } known)
            return known;
        var entry = LibraryGame.For(found, now);
        Removed.RemoveAll(id => string.Equals(id, entry.Id, StringComparison.OrdinalIgnoreCase));
        Games.Add(entry);
        return entry;
    }

    /// <summary>Takes a game off the list; its mods and their records stay, and adding the game again brings them back.</summary>
    public void Remove(LibraryGame game)
    {
        Games.Remove(game);
        if (!Removed.Contains(game.Id, StringComparer.OrdinalIgnoreCase))
            Removed.Add(game.Id);
    }

    /// <summary>Saves the library; a failure is logged, as the library is rebuilt from the games found when it is lost.</summary>
    public bool TrySave(AppPaths paths)
    {
        try
        {
            JsonFile.Write(paths.LibraryFile, this);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("The game library could not be saved.", ex);
            return false;
        }
    }
}
