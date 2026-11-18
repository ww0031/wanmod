using Microsoft.Win32;

namespace UEManager.Core.Stores;

/// <param name="Name">The name Steam gives the app; null when its manifest has none.</param>
/// <param name="LastPlayed">When the app last ran, as Steam noted it; null when never.</param>
public sealed record SteamApp(string AppId, string InstallPath, string? BuildId, string? Name = null, DateTimeOffset? LastPlayed = null);

/// <summary>What an app manifest (appmanifest_&lt;id&gt;.acf) says of an installed app.</summary>
internal sealed record SteamAppManifest(string InstallDir, string? BuildId, string? AppId, string? Name, DateTimeOffset? LastPlayed);

/// <summary>The pictures Steam keeps of an app for its library: the tall cover and the wide background.</summary>
/// <param name="Cover">A file on this PC, else the store's address of the cover.</param>
/// <param name="Hero">A file on this PC; null when Steam keeps none.</param>
public sealed record SteamArt(string Cover, string? Hero);

/// <summary>Reads Steam's library list and app manifests. Never writes to Steam.</summary>
public static class SteamLibrary
{
    public static string LaunchUri(string appId) => $"steam://rungameid/{appId}";

    /// <summary>Finds an installed app in any Steam library on this machine.</summary>
    public static SteamApp? FindApp(string appId, IEnumerable<string>? steamRoots = null) =>
        Libraries(steamRoots).Select(library => FindAppInLibrary(library, appId)).FirstOrDefault(app => app is not null);

    /// <summary>Every installed app in every Steam library on this machine, each once.</summary>
    public static IReadOnlyList<SteamApp> InstalledApps(IEnumerable<string>? steamRoots = null)
    {
        var apps = new List<SteamApp>();
        foreach (var library in Libraries(steamRoots))
        {
            string[] manifests;
            try
            {
                var folder = Path.Combine(library, "steamapps");
                manifests = Directory.Exists(folder) ? Directory.GetFiles(folder, "appmanifest_*.acf") : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Steam library '{library}' could not be read: {ex.Message}");
                continue;
            }
            foreach (var manifest in manifests)
            {
                if (ReadManifest(library, manifest) is { } app && apps.All(other => other.AppId != app.AppId))
                    apps.Add(app);
            }
        }
        return apps;
    }

    /// <summary>The Steam folders, then the libraries Steam lists in them, each once.</summary>
    private static IEnumerable<string> Libraries(IEnumerable<string>? steamRoots)
    {
        steamRoots ??= DefaultSteamRoots();
        var libraries = new List<string>();
        foreach (var root in steamRoots.Where(Directory.Exists))
        {
            libraries.Add(root);
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;
            try
            {
                libraries.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
            }
            catch (Exception ex) when (ex is IOException or FormatException)
            {
                Log.Warn($"Steam library list '{vdf}' could not be read: {ex.Message}");
            }
        }
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Reads the app manifest of the Steam library a game folder sits in, if it sits in one.</summary>
    public static SteamApp? FindAppInLibraryOf(string gamePath, string appId)
    {
        for (var dir = new DirectoryInfo(gamePath); dir is not null; dir = dir.Parent)
        {
            if (dir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                return dir.Parent is null ? null : FindAppInLibrary(dir.Parent.FullName, appId);
        }
        return null;
    }

    /// <summary>The app installed in this folder, if the folder is one of a Steam library's games.</summary>
    public static SteamApp? FindAppAt(string installFolder)
    {
        for (var dir = new DirectoryInfo(installFolder); dir is not null; dir = dir.Parent)
        {
            if (dir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                return dir.Parent is null
                    ? null
                    : InstalledApps([dir.Parent.FullName]).FirstOrDefault(app => FileSystem.IsSameFolder(app.InstallPath, installFolder));
        }
        return null;
    }

    /// <summary>
    /// The library pictures of an app: those Steam keeps on this PC, in librarycache as &lt;id&gt;_library_600x900.jpg
    /// or, since 2024, in librarycache\&lt;id&gt;\&lt;hash&gt;\; else the store's cover.
    /// </summary>
    public static SteamArt LibraryArt(string appId, IEnumerable<string>? steamRoots = null)
    {
        string? cover = null, hero = null;
        foreach (var cache in (steamRoots ?? DefaultSteamRoots()).Select(root => Path.Combine(root, "appcache", "librarycache")))
        {
            try
            {
                if (!Directory.Exists(cache))
                    continue;
                cover ??= Existing(Path.Combine(cache, $"{appId}_library_600x900.jpg"));
                hero ??= Existing(Path.Combine(cache, $"{appId}_library_hero.jpg"));
                var folder = Path.Combine(cache, appId);
                if (Directory.Exists(folder))
                {
                    var files = Directory.GetFiles(folder, "*.jpg", SearchOption.AllDirectories);
                    cover ??= files.FirstOrDefault(f => Path.GetFileName(f).StartsWith("library_600x900", StringComparison.OrdinalIgnoreCase))
                        ?? files.FirstOrDefault(f => Path.GetFileName(f).StartsWith("library_capsule", StringComparison.OrdinalIgnoreCase));
                    hero ??= files.FirstOrDefault(f => Path.GetFileName(f).StartsWith("library_hero", StringComparison.OrdinalIgnoreCase)
                        && !Path.GetFileName(f).Contains("blur", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Steam's pictures of app {appId} could not be read: {ex.Message}");
            }
        }
        return new SteamArt(cover ?? $"https://shared.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg", hero);
    }

    private static string? Existing(string file) => File.Exists(file) ? file : null;

    private static SteamApp? FindAppInLibrary(string library, string appId) =>
        ReadManifest(library, Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf"), appId);

    /// <param name="appId">The app's id, as the manifest's file name gives it; null to take the one inside.</param>
    private static SteamApp? ReadManifest(string library, string manifest, string? appId = null)
    {
        if (!File.Exists(manifest))
            return null;
        try
        {
            var parsed = ParseAppManifest(File.ReadAllText(manifest));
            if (parsed is null || (appId ?? parsed.AppId) is not { Length: > 0 } id)
                return null;
            var install = Path.Combine(library, "steamapps", "common", parsed.InstallDir);
            return Directory.Exists(install) ? new SteamApp(id, install, parsed.BuildId, parsed.Name, parsed.LastPlayed) : null;
        }
        catch (Exception ex) when (ex is IOException or FormatException)
        {
            Log.Warn($"Steam manifest '{manifest}' could not be read: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Library paths from libraryfolders.vdf. Current Steam writes "0" { "path" "D:\\SteamLibrary" ... };
    /// older versions wrote "1" "D:\\SteamLibrary" directly.
    /// </summary>
    internal static IReadOnlyList<string> ParseLibraryFolders(string vdf)
    {
        var folders = VdfNode.Parse(vdf).Child("libraryfolders");
        if (folders is null)
            return [];
        return folders.Children
            .Where(entry => entry.Key.All(char.IsAsciiDigit))
            .Select(entry => entry.Value.Value ?? entry.Value.Text("path"))
            .OfType<string>()
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static SteamAppManifest? ParseAppManifest(string acf)
    {
        var state = VdfNode.Parse(acf).Child("AppState");
        var installDir = state?.Text("installdir");
        if (string.IsNullOrEmpty(installDir))
            return null;
        DateTimeOffset? played = long.TryParse(state!.Text("LastPlayed"), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        return new SteamAppManifest(installDir, state.Text("buildid"), state.Text("appid"), state.Text("name"), played);
    }

    /// <summary>Where Steam is installed on this machine, as the registry says, else its usual folder.</summary>
    public static IEnumerable<string> DefaultSteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string userPath)
                yield return Path.GetFullPath(userPath);
            if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) is string machinePath)
                yield return machinePath;
        }
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
    }
}
