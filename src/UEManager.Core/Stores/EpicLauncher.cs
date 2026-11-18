using System.Text.Json;

namespace UEManager.Core.Stores;

/// <param name="DisplayName">The name the Epic Games Launcher shows for the app.</param>
/// <param name="AppName">The launcher's own name of the app, such as "Fortnite"; null when its manifest has none.</param>
public sealed record EpicApp(string DisplayName, string InstallPath, string LaunchUri, string? Version, string? AppName = null);

/// <summary>Reads the Epic Games Launcher's install manifests. Never writes to them.</summary>
public static class EpicLauncher
{
    public static string DefaultManifestsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

    /// <summary>The installed app with this name, as the Epic Games Launcher shows it.</summary>
    public static EpicApp? FindApp(string displayName, string? manifestsFolder = null) =>
        Apps(manifestsFolder).FirstOrDefault(app => string.Equals(app.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The installed app in this folder, whatever its name.</summary>
    public static EpicApp? FindAppIn(string installFolder, string? manifestsFolder = null) =>
        Apps(manifestsFolder).FirstOrDefault(app => FileSystem.IsSameFolder(app.InstallPath, installFolder));

    /// <summary>The installed games: the Unreal Engine itself, its plugins and a game's add-ons are left out.</summary>
    public static IReadOnlyList<EpicApp> InstalledGames(string? manifestsFolder = null) =>
        Apps(manifestsFolder, gamesOnly: true).ToList();

    /// <summary>What the launcher files under these categories is no game of its own.</summary>
    private static readonly string[] OtherCategories = ["engines", "plugins", "addons", "digitalextras"];

    /// <summary>The installed apps whose install folder exists. A manifest that cannot be read is skipped.</summary>
    private static IEnumerable<EpicApp> Apps(string? manifestsFolder, bool gamesOnly = false)
    {
        manifestsFolder ??= DefaultManifestsFolder;
        if (!Directory.Exists(manifestsFolder))
            yield break;
        foreach (var file in Directory.EnumerateFiles(manifestsFolder, "*.item"))
        {
            EpicApp? app = null;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && Text(root, "InstallLocation") is { } install && Directory.Exists(install)
                    && !(gamesOnly && IsOther(root)))
                {
                    var launch = $"com.epicgames.launcher://apps/{Text(root, "CatalogNamespace")}%3A{Text(root, "CatalogItemId")}%3A{Text(root, "AppName")}?action=launch&silent=true";
                    app = new EpicApp(Text(root, "DisplayName") ?? "", install, launch, Text(root, "AppVersionString"), Text(root, "AppName"));
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Log.Warn($"Epic manifest '{file}' could not be read: {ex.Message}");
            }
            if (app is not null)
                yield return app;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsOther(JsonElement root) =>
        root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array
        && categories.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String
            && OtherCategories.Contains(c.GetString(), StringComparer.OrdinalIgnoreCase));
}
