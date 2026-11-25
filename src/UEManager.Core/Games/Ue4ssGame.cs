namespace UEManager.Core.Games;

/// <summary>
/// An Unreal game without a profile of its own whose engine was changed, so the general UE4SS closes it at start: it
/// needs the UE4SS build made for it, which the manifest lists under <paramref name="ManifestId"/>. The game is known by
/// its Steam app id, or by its project folder for a copy from another store or a folder picked by hand, and keeps its
/// id in the library, so its mods stay as they are.
/// </summary>
/// <param name="PackageUrl">That build, for when the manifest cannot be reached, with its SHA-256 and its version.</param>
/// <param name="LaunchArguments">What the game must be started with to load pak mods; null for nothing.</param>
public sealed record Ue4ssGame(string SteamAppId, string ProjectFolder, string ManifestId, string PackageUrl, string PackageSha256,
    string? PackageVersion = null, string? LaunchArguments = null)
{
    /// <summary>Where these builds are published: the page "How to install UE4SS" opens for such a game.</summary>
    public const string Releases = "https://github.com/KintaroModding/ue4ss/releases";

    /// <summary>Each file's SHA-256 is the one in SHA256SUMS.txt of its release, checked against the download on 2026-11-26.</summary>
    public static readonly IReadOnlyList<Ue4ssGame> Known =
    [
        new("2358720", "b1", "black-myth-wukong",
            Releases + "/download/v3.0.1-1195-g1ceefac8/blackmythwukong_ue4ss_v3.0.1-1195-g1ceefac8.zip",
            "68DA5D7FECF5ECEA34B645462B319070A38137A6095457077B7955FCC7664017", LaunchArguments: "-fileopenlog"),
        new("2277560", "Project_Plague", "wuchang-fallen-feathers",
            Releases + "/download/v4.0.0/wuchang_ue4ss_v3.0.1-934-gcac01ee2.zip",
            "C4DE5CD0E378E3D47B27DCD4A1FA6AB8FFD5071E3DF63CB6B74AC924BF956657"),
        new("3489700", "SB", "stellar-blade",
            Releases + "/download/v4.0.0/stellarblade_ue4ss_sb-ue4ss-1.3.zip",
            "C4D814FB55482C45656F4D0E010813F21919B1C875D3D191D416394DA207CFB8", PackageVersion: "sb-ue4ss-1.3"),
    ];

    /// <summary>The entry of a game with this Steam app id or this project folder; null for every other game.</summary>
    public static Ue4ssGame? Find(string? steamAppId, string? projectFolder) =>
        Known.FirstOrDefault(g => g.SteamAppId == steamAppId)
        ?? Known.FirstOrDefault(g => string.Equals(g.ProjectFolder, projectFolder, StringComparison.OrdinalIgnoreCase));
}
