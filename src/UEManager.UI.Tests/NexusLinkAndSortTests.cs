using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>A mod's link dragged into the window is taken as one, and the Browse page keeps its order between starts.</summary>
public class NexusLinkAndSortTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-link-sort-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/phantombladezero/mods/260", true)]
    [InlineData("https://www.nexusmods.com/phantombladezero/mods/260?tab=files", true)]
    [InlineData("https://www.nexusmods.com/games/phantombladezero", false)]
    [InlineData("some words from a page", false)]
    public void Dragged_text_can_be_installed_only_when_it_is_the_link_of_a_mod(string text, bool isLink) => UiThread.Run(() =>
    {
        var data = new DataObject(DataFormats.UnicodeText, text);
        Assert.Equal(isLink ? text : null, MainWindow.DraggedLink(data));
        Assert.Equal(isLink, MainWindow.CanInstall(data));
    });

    [Fact]
    public void Dragged_files_can_be_installed_and_are_no_link() => UiThread.Run(() =>
    {
        var data = new DataObject(DataFormats.FileDrop, new[] { @"C:\mods\a.zip" });
        Assert.True(MainWindow.CanInstall(data));
        Assert.Null(MainWindow.DraggedLink(data));
    });

    [Fact]
    public void The_browse_page_opens_in_the_order_chosen_last_time() => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        using var http = new HttpClient(new EmptyList());
        var translator = new FreeTranslator(http, Path.Combine(folder, "translations.json"));
        var first = new NexusBrowseViewModel(Model(paths), http, translator, "phantombladezero");
        Assert.True(first.SortOptions[0].IsActive);

        first.SortOptions[1].Select.Execute(null);

        Assert.Equal(NexusSort.Downloads, AppSettings.Load(paths).BrowseSort);
        var second = new NexusBrowseViewModel(Model(paths), http, translator, "phantombladezero");
        Assert.False(second.SortOptions[0].IsActive);
        Assert.True(second.SortOptions[1].IsActive);
    });

    private static MainViewModel Model(AppPaths paths) =>
        new(new GameProfile { Id = "test", DisplayName = "Test" }, paths, new ArtSet(null, null));

    /// <summary>Nexus Mods with no mods for the game, so the page's load ends without the network.</summary>
    private sealed class EmptyList : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":{"mods":{"totalCount":0,"nodes":[]}}}""", Encoding.UTF8, "application/json"),
            });
    }
}
