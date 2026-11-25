using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>A game that needs its own UE4SS build: what the one-click install offers while the manifest has not answered.</summary>
public sealed class Ue4ssGameTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "uemm-ue4ss-game-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Theory]
    [InlineData("2358720", "b1", "v3.0.1-1195-g1ceefac8", "blackmythwukong_ue4ss_v3.0.1-1195-g1ceefac8.zip")]
    [InlineData("2277560", "Project_Plague", "v3.0.1-934-gcac01ee2", "wuchang_ue4ss_v3.0.1-934-gcac01ee2.zip")]
    [InlineData("3489700", "SB", "sb-ue4ss-1.3", "stellarblade_ue4ss_sb-ue4ss-1.3.zip")]
    public void Its_own_build_is_offered_with_its_version(string steamAppId, string project, string version, string file) => UiThread.Run(() =>
    {
        var profile = new LibraryGame { Id = "steam-" + steamAppId, Name = "Game", SteamAppId = steamAppId, Unreal = true, ProjectFolder = project }.ToProfile();
        var paths = new AppPaths(Path.Combine(root, "Data"));
        paths.EnsureWritable();

        var offered = new MainViewModel(profile, paths, new ArtSet(null, null)).AvailableUe4ss;

        Assert.NotNull(offered);
        Assert.Equal(version, offered.Version);
        Assert.Equal(file, offered.FileName);
        Assert.Equal(profile.Ue4ssPackageSha256, offered.Sha256);
    });

    [Fact]
    public void Another_unreal_game_is_offered_none() => UiThread.Run(() =>
    {
        var profile = new LibraryGame { Id = "steam-100", Name = "Game", SteamAppId = "100", Unreal = true, ProjectFolder = "Shooter" }.ToProfile();
        var paths = new AppPaths(Path.Combine(root, "Data"));
        paths.EnsureWritable();

        Assert.Null(new MainViewModel(profile, paths, new ArtSet(null, null)).AvailableUe4ss);
    });
}
