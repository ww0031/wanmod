using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.Core.Stores;

namespace UEManager.Core.Tests;

/// <summary>The game library: finding installed games, starting the library from the games of earlier versions, and generic mode.</summary>
public class GameLibraryTests : IDisposable
{
    private readonly TestFolder folder = new();

    public void Dispose() => folder.Dispose();

    /// <summary>A Steam folder with one library, holding an app's manifest and its install folder.</summary>
    private string SteamApp(string appId, string name, string installDir, long lastPlayed = 0)
    {
        folder.File($@"Steam\steamapps\appmanifest_{appId}.acf", $$"""
            "AppState"
            {
                "appid"      "{{appId}}"
                "name"       "{{name}}"
                "installdir" "{{installDir}}"
                "buildid"    "1"
                "LastPlayed" "{{lastPlayed}}"
            }
            """);
        return folder.Combine("Steam");
    }

    [Fact]
    public void A_scan_lists_steam_games_tells_unreal_games_apart_and_leaves_out_tools()
    {
        var steam = SteamApp("100", "Unreal Game", "Unreal Game", 1_700_000_000);
        folder.Game("Shooter", @"Steam\steamapps\common\Unreal Game");
        SteamApp("200", "Other Game", "Other Game");
        folder.File(@"Steam\steamapps\common\Other Game\Other.exe");
        SteamApp("228980", "Steamworks Common Redistributables", "Steamworks Shared");
        folder.File(@"Steam\steamapps\common\Steamworks Shared\_CommonRedist\setup.exe");
        SteamApp("300", "Soundtrack", "Soundtrack");
        folder.File(@"Steam\steamapps\common\Soundtrack\01.flac");

        var games = GameScanner.Scan([steam], folder.Combine("NoEpic"));

        Assert.Equal(["Other Game", "Unreal Game"], games.Select(g => g.Name).Order());
        var unreal = Assert.Single(games, g => g.SteamAppId == "100");
        Assert.Equal("Shooter", unreal.ProjectFolder);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), unreal.LastPlayed);
        Assert.False(Assert.Single(games, g => g.SteamAppId == "200").Unreal);
    }

    [Fact]
    public void A_picked_binaries_folder_adds_the_unreal_game_it_is_in()
    {
        var root = folder.Game("Shooter", "Picked Game");

        var found = GameScanner.FromFolder(Path.Combine(root, "Shooter", "Binaries", "Win64"), folder.Combine("NoEpic"));

        Assert.NotNull(found);
        Assert.Equal(root, found.RootPath);
        Assert.Equal("Shooter", found.ProjectFolder);
        Assert.Equal(GameStore.Manual, found.Store);
        Assert.StartsWith("folder-", LibraryGame.For(found, DateTimeOffset.Now).Id);
    }

    [Fact]
    public void A_folder_without_an_exe_is_no_game()
    {
        folder.File(@"Music\01.flac");

        Assert.Null(GameScanner.FromFolder(folder.Combine("Music")));
    }

    [Fact]
    public void The_first_library_holds_the_built_in_game_where_the_player_chose_it_before()
    {
        var root = folder.Game("ProjectPBZ", "PBZ");
        folder.File(@"PBZ\platform\steam\PBZ.exe");
        folder.File(@"PBZ\steam\PBZ.exe");
        var paths = new AppPaths(folder.Combine("Data"));
        var settings = new AppSettings();
        settings.GamePaths["phantom-blade-zero"] = root;

        var library = GameLibrary.Load(paths, settings, _ => null);

        var game = Assert.Single(library.Games);
        Assert.Equal("phantom-blade-zero", game.Id);
        Assert.Equal(root, game.RootPath);
        Assert.Equal("phantombladezero", game.NexusDomain);
        Assert.True(File.Exists(paths.LibraryFile));
        Assert.Equal(root, GameLibrary.Load(paths, new AppSettings(), _ => null).Games.Single().RootPath);
    }

    [Fact]
    public void A_game_not_made_with_unreal_installs_its_download_as_it_is_into_its_mods_folder()
    {
        folder.File(@"Other\Other.exe");
        folder.File(@"Other\Mods\Shared\keep.txt", "another mod's");
        var entry = LibraryGame.For(GameScanner.FromFolder(folder.Combine("Other"))!, DateTimeOffset.Now);
        entry.ModsFolder = "Mods";
        var game = entry.Locate()!;
        var manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
        folder.File(@"Download\BetterUI\plugin.dll", "dll");
        folder.File(@"Download\BetterUI\config\ui.cfg", "cfg");
        folder.File(@"Download\BetterUI\README.txt", "readme");

        ModRecord mod;
        using (var preview = manager.Inspect(folder.Combine("Download", "BetterUI")))
        {
            var part = Assert.Single(preview.Packages);
            Assert.Equal(["Mods/BetterUI/config/ui.cfg", "Mods/BetterUI/plugin.dll"], part.RelativePaths);
            Assert.Equal(["BetterUI/README.txt"], preview.IgnoredFiles);
            mod = manager.Install(preview, preview.Packages.ToList(), "Better UI", "1.0");
        }

        Assert.Equal("dll", File.ReadAllText(folder.Combine(@"Other\Mods\BetterUI\plugin.dll")));
        Assert.Empty(manager.UnmanagedFiles());

        manager.Uninstall(mod.Id);

        Assert.False(Directory.Exists(folder.Combine(@"Other\Mods\BetterUI")));
        Assert.True(File.Exists(folder.Combine(@"Other\Mods\Shared\keep.txt")));
        Assert.True(Directory.Exists(folder.Combine(@"Other\Mods")));
    }

    [Fact]
    public void A_game_that_needs_its_own_ue4ss_build_keeps_its_id_and_is_known_by_its_steam_id_or_its_project_folder()
    {
        var steam = new LibraryGame { Id = "steam-2358720", Name = "Black Myth: Wukong", SteamAppId = "2358720", Unreal = true, ProjectFolder = "b1" }.ToProfile();
        var picked = new LibraryGame { Id = "folder-0a1b2c", Name = "Wukong", Unreal = true, ProjectFolder = "B1" }.ToProfile();
        var blade = new LibraryGame { Id = "epic-sb", Name = "Stellar Blade", Unreal = true, ProjectFolder = "SB" }.ToProfile();
        var other = new LibraryGame { Id = "steam-100", Name = "Other", SteamAppId = "100", Unreal = true, ProjectFolder = "Shooter" }.ToProfile();
        var generic = new LibraryGame { Id = "steam-2358720", Name = "Black Myth: Wukong", SteamAppId = "2358720" }.ToProfile();

        Assert.Equal("steam-2358720", steam.Id);
        Assert.Equal("black-myth-wukong", steam.Ue4ssGameId);
        Assert.Equal("-fileopenlog", steam.LaunchArguments);
        Assert.EndsWith("/blackmythwukong_ue4ss_v3.0.1-1195-g1ceefac8.zip", steam.Ue4ssPackageUrl);
        Assert.Equal(64, steam.Ue4ssPackageSha256!.Length);
        Assert.Equal(Ue4ssGame.Releases, steam.Ue4ssDownloadUrl);
        Assert.Equal("folder-0a1b2c", picked.Id);
        Assert.Equal("black-myth-wukong", picked.Ue4ssGameId);
        Assert.Equal("stellar-blade", blade.Ue4ssGameId);
        Assert.Equal("sb-ue4ss-1.3", blade.Ue4ssPackageVersion);
        Assert.Null(blade.LaunchArguments);
        Assert.Null(other.Ue4ssGameId);
        Assert.Null(other.Ue4ssPackageUrl);
        Assert.Null(other.Ue4ssDownloadUrl);
        Assert.Null(other.LaunchArguments);
        Assert.True(generic.Generic);
        Assert.Null(generic.Ue4ssGameId);
        Assert.Null(generic.LaunchArguments);
    }

    [Fact]
    public void A_game_with_start_arguments_starts_from_its_own_exe_with_them_also_for_a_store_copy()
    {
        var root = folder.Game("b1", "Wukong");
        var profile = new LibraryGame { Id = "steam-2358720", Name = "Black Myth: Wukong", RootPath = root, SteamAppId = "2358720", Unreal = true, ProjectFolder = "b1" }.ToProfile();
        var exe = Path.Combine(root, "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe");

        var withArguments = GameLocator.Resolve(profile, root, GameStore.Steam, "1", SteamLibrary.LaunchUri("2358720"))!.LaunchCommand();
        var plain = GameLocator.Resolve(TestFolder.Profile("b1"), root, GameStore.Steam, "1", SteamLibrary.LaunchUri("999999"))!.LaunchCommand();
        var byHand = GameLocator.Resolve(TestFolder.Profile("b1"), root, GameStore.Manual)!.LaunchCommand();

        Assert.Equal(exe, withArguments.Target);
        Assert.Equal("-fileopenlog", withArguments.Arguments);
        Assert.Equal(Path.GetDirectoryName(exe), withArguments.Folder);
        Assert.Equal("steam://rungameid/999999", plain.Target);
        Assert.Null(plain.Arguments);
        Assert.Null(plain.Folder);
        Assert.Equal(Path.Combine(root, "b1.exe"), byHand.Target);
        Assert.Null(byHand.Arguments);
        Assert.Equal(root, byHand.Folder);
    }
}
