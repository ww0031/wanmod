using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The changelog at start: it opens until "Do not show again" is ticked, and the tick holds for one version.</summary>
public class ChangelogAtStartTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-changelog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    /// <summary>Starts the app on the settings an earlier start left, and gives the settings this start leaves.</summary>
    private void Start(AppSettings? earlier, Action<AppUpdateViewModel, AppSettings> check) => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        earlier?.Save(paths);
        var model = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Test" }, paths, new ArtSet(null, null));
        Assert.True(model.Update.OpenChangelogAtStartAsync().IsCompleted);
        check(model.Update, AppSettings.Load(paths));
    });

    [Fact]
    public void It_opens_on_the_first_start() => Start(null, (update, settings) =>
    {
        Assert.True(update.IsChangelogOpen);
        Assert.Equal(AppInfo.Version, settings.ShownChangelog);
    });

    [Fact]
    public void It_opens_again_while_do_not_show_again_is_not_ticked() =>
        Start(new AppSettings { ShownChangelog = AppInfo.Version }, (update, _) => Assert.True(update.IsChangelogOpen));

    [Fact]
    public void It_stays_closed_in_the_version_where_do_not_show_again_was_ticked() =>
        Start(new AppSettings { ShownChangelog = AppInfo.Version, HideChangelog = true }, (update, settings) =>
        {
            Assert.False(update.IsChangelogOpen);
            Assert.True(settings.HideChangelog);
        });

    [Fact]
    public void It_opens_after_an_update_and_the_tick_of_the_older_version_is_gone() =>
        Start(new AppSettings { ShownChangelog = "0.1", HideChangelog = true }, (update, settings) =>
        {
            Assert.True(update.IsChangelogOpen);
            Assert.False(update.HideChangelog);
            Assert.False(settings.HideChangelog);
            Assert.Equal(AppInfo.Version, settings.ShownChangelog);
        });
}
