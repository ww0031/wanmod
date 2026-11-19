using UEManager.Core.Mods;

namespace UEManager.Core.Tests;

public class StoredPackagesTests
{
    [Fact]
    public void A_download_moves_into_the_folder_and_one_already_there_stays()
    {
        using var folder = new TestFolder();
        var download = folder.File(@"downloads\Hair 414 1.0 f3643.zip", "zip");
        var packages = folder.Combine("packages");

        StoredPackages.Store(download, packages);
        var stored = Path.Combine(packages, "Hair 414 1.0 f3643.zip");
        StoredPackages.Store(stored, packages);

        Assert.False(File.Exists(download));
        Assert.Equal("zip", File.ReadAllText(stored));
    }

    [Fact]
    public void Only_the_downloads_no_installed_mod_came_from_are_deleted()
    {
        using var folder = new TestFolder();
        var kept = folder.File(@"packages\Hair 414 1.0 f3643.zip");
        var updated = folder.File(@"packages\Hair 414 0.9 f3000.zip");
        var uninstalled = folder.File(@"packages\Coat 500 2 f4000.7z");
        var other = folder.File(@"library\mod\Hair_P.pak");

        StoredPackages.Sweep(folder.Combine("packages"),
            [new ModRecord { SourceFileName = "hair 414 1.0 F3643.zip" }, new ModRecord { SourceFileName = null }]);

        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(updated));
        Assert.False(File.Exists(uninstalled));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void A_folder_that_is_not_there_is_left_alone()
    {
        using var folder = new TestFolder();

        StoredPackages.Sweep(folder.Combine("packages"), []);

        Assert.False(Directory.Exists(folder.Combine("packages")));
    }
}
