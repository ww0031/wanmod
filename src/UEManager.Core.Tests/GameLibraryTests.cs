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
}
