using UEManager.Core.Stores;

namespace UEManager.Core.Games;

/// <summary>An installed game, as a store lists it or as a folder the player picked holds it.</summary>
/// <param name="ProjectFolder">The Unreal project folder, the one holding Content\Paks; null for a game not made with Unreal Engine.</param>
/// <param name="LastPlayed">When the game last ran, as Steam noted it; null when unknown.</param>
public sealed record FoundGame(string Name, string RootPath, GameStore Store, string? SteamAppId, string? EpicAppName,
    string? ProjectFolder, DateTimeOffset? LastPlayed = null)
{
    public bool Unreal => ProjectFolder is not null;
}

/// <summary>Finds the games installed through Steam and the Epic Games Launcher, and tells Unreal games from others.</summary>
public static class GameScanner
{
    /// <summary>Steam's tools, which install like games: runtimes, compatibility layers, redistributables.</summary>
    private static readonly string[] ToolNames = ["Steamworks Common Redistributables", "Steam Linux Runtime", "Proton", "SteamVR"];

    /// <summary>How deep below an install folder a game's exe may be, as in Binaries\Win64.</summary>
    private const int ExeDepth = 3;

    /// <summary>The installed games: Steam's first, then Epic's not already listed. Tools and folders without an exe are left out.</summary>
    public static IReadOnlyList<FoundGame> Scan(IEnumerable<string>? steamRoots = null, string? epicManifestsFolder = null)
    {
        var found = new List<FoundGame>();
        foreach (var app in SteamLibrary.InstalledApps(steamRoots))
        {
            var name = app.Name is { Length: > 0 } named ? named : Path.GetFileName(app.InstallPath);
            if (ToolNames.Any(tool => name.StartsWith(tool, StringComparison.OrdinalIgnoreCase)) || !HasExecutable(app.InstallPath))
                continue;
            found.Add(new FoundGame(name, Path.GetFullPath(app.InstallPath), GameStore.Steam, app.AppId, null,
                UnrealProject(app.InstallPath), app.LastPlayed));
        }
        foreach (var app in EpicLauncher.InstalledGames(epicManifestsFolder))
        {
            if (found.Any(game => FileSystem.IsSameFolder(game.RootPath, app.InstallPath)) || !HasExecutable(app.InstallPath))
                continue;
            var name = app.DisplayName.Length > 0 ? app.DisplayName : Path.GetFileName(Path.TrimEndingDirectorySeparator(app.InstallPath));
            found.Add(new FoundGame(name, Path.GetFullPath(app.InstallPath), GameStore.Epic, null, app.AppName, UnrealProject(app.InstallPath)));
        }
        return found;
    }

    /// <summary>
    /// The game in a folder the player picked: the Unreal game the folder is in, as when Binaries\Win64 was picked, else
    /// the folder itself. A store's install keeps the store's name for it.
    /// </summary>
    /// <returns>Null when the folder holds no exe at all, so it cannot be a game.</returns>
    public static FoundGame? FromFolder(string folder, string? epicManifestsFolder = null)
    {
        var picked = Path.GetFullPath(folder);
        var root = UnrealRoot(picked) ?? picked;
        if (!HasExecutable(root))
            return null;
        var project = UnrealProject(root);
        if (SteamLibrary.FindAppAt(root) is { } steam)
            return new FoundGame(steam.Name ?? Path.GetFileName(root), root, GameStore.Steam, steam.AppId, null, project, steam.LastPlayed);
        if (EpicLauncher.FindAppIn(root, epicManifestsFolder) is { } epic)
            return new FoundGame(epic.DisplayName.Length > 0 ? epic.DisplayName : Path.GetFileName(root), root, GameStore.Epic, null,
                epic.AppName, project);
        return new FoundGame(Path.GetFileName(Path.TrimEndingDirectorySeparator(root)), root, GameStore.Manual, null, null, project);
    }

    /// <summary>
    /// The Unreal project folder of a game: the first folder in it, the Engine folder aside, that holds Content\Paks,
    /// where Unreal games keep their packages. Null for a game not made with Unreal Engine.
    /// </summary>
    public static string? UnrealProject(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.GetDirectories(root)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .Where(folder => !Path.GetFileName(folder).Equals("Engine", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(folder => Directory.Exists(Path.Combine(folder, "Content", "Paks"))) is { } project
                    ? Path.GetFileName(project)
                    : null
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The Unreal game a folder is in, up to three levels up; null when it is in none.</summary>
    private static string? UnrealRoot(string folder)
    {
        var candidate = new DirectoryInfo(folder);
        for (var level = 0; candidate is not null && level <= 3; level++, candidate = candidate.Parent)
        {
            if (UnrealProject(candidate.FullName) is not null && (level == 0 || Directory.Exists(Path.Combine(candidate.FullName, "Engine"))))
                return candidate.FullName;
        }
        return null;
    }

    /// <summary>Whether a folder holds an exe, up to <see cref="ExeDepth"/> levels down: a game does, a soundtrack or a tool's data does not.</summary>
    internal static bool HasExecutable(string folder)
    {
        try
        {
            return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.exe",
                new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = ExeDepth, IgnoreInaccessible = true }).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
