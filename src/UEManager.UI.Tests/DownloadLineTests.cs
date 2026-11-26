using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The line of downloads at the bottom right of the window: one runs, the others wait their turn.</summary>
public sealed class DownloadLineTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-line-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private void With(Action<MainViewModel> check) => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        check(new MainViewModel(new GameProfile { Id = "test", DisplayName = "Test" }, paths, new ArtSet(null, null)));
    });

    /// <summary>A download joins the line, as a click on Install does.</summary>
    private static DownloadItemViewModel Add(MainViewModel model, string name)
    {
        var item = new DownloadItemViewModel(name, name, name + ".zip", 1000, model.PumpDownloads, model.MoveDownloadFirst);
        model.Downloads.Add(item);
        model.PumpDownloads();
        return item;
    }

    /// <summary>A download leaves the line, as one that is done or taken out does.</summary>
    private static void Remove(MainViewModel model, DownloadItemViewModel item)
    {
        model.Downloads.Remove(item);
        model.PumpDownloads();
    }

    private static string[] Line(MainViewModel model) => model.Downloads.Select(d => $"{d.Name}:{d.State}:{d.Place}").ToArray();

    [Fact]
    public void One_download_runs_and_the_others_wait_in_order() => With(model =>
    {
        var a = Add(model, "A");
        var b = Add(model, "B");
        var c = Add(model, "C");

        Assert.Equal(["A:Running:0", "B:Waiting:1", "C:Waiting:2"], Line(model));
        Assert.True(a.WaitForTurnAsync().IsCompletedSuccessfully);
        Assert.False(b.WaitForTurnAsync().IsCompleted);
        Assert.Equal("Waiting  ·  next in line", b.Detail);
        Assert.Equal("Waiting  ·  number 2 in line", c.Detail);
        Assert.Equal([true, false, false], new[] { a, b, c }.Select(d => d.ShowsProgress));
        Assert.Equal([false, false, true], new[] { a, b, c }.Select(d => d.CanMoveFirst));
    });

    [Fact]
    public void A_download_that_is_done_gives_its_turn_to_the_next() => With(model =>
    {
        var a = Add(model, "A");
        var b = Add(model, "B");
        var c = Add(model, "C");

        Remove(model, a);

        Assert.Equal(["B:Running:0", "C:Waiting:1"], Line(model));
        Assert.True(b.WaitForTurnAsync().IsCompletedSuccessfully);
        Assert.False(c.WaitForTurnAsync().IsCompleted);
    });

    [Fact]
    public void A_pause_ends_the_transfer_and_lets_the_next_one_run() => With(model =>
    {
        var a = Add(model, "A");
        Add(model, "B");
        var transfer = a.TransferToken;
        a.Report(0.4);
        Assert.EndsWith("/s", a.Detail);

        a.PauseCommand.Execute(null);

        Assert.True(transfer.IsCancellationRequested);
        Assert.False(a.Token.IsCancellationRequested);
        Assert.Equal(["A:Paused:0", "B:Running:0"], Line(model));
        Assert.False(a.WaitForTurnAsync().IsCompleted);
        Assert.StartsWith("Paused  ·  40", a.Detail);
        Assert.True(a.CanContinue);
        Assert.False(a.CanPause);
        Assert.Equal("Continue the download", a.ContinueTip);
        Assert.True(a.ShowsProgress);

        // Progress that was on its way when the pause came moves the bar, and the text stays.
        a.Report(0.5);
        Assert.StartsWith("Paused  ·  40", a.Detail);
    });

    [Fact]
    public void A_continued_download_waits_its_turn_and_then_runs_again() => With(model =>
    {
        var a = Add(model, "A");
        var b = Add(model, "B");
        a.PauseCommand.Execute(null);

        a.ContinueCommand.Execute(null);

        Assert.Equal(["A:Waiting:1", "B:Running:0"], Line(model));
        Assert.False(a.WaitForTurnAsync().IsCompleted);

        Remove(model, b);

        Assert.Equal(["A:Running:0"], Line(model));
        Assert.True(a.WaitForTurnAsync().IsCompletedSuccessfully);
        Assert.False(a.TransferToken.IsCancellationRequested);
    });

    [Fact]
    public void A_download_that_waits_can_be_paused_and_the_others_move_up() => With(model =>
    {
        Add(model, "A");
        var b = Add(model, "B");
        Add(model, "C");

        b.PauseCommand.Execute(null);

        Assert.Equal(["A:Running:0", "B:Paused:0", "C:Waiting:1"], Line(model));
        Assert.False(b.CanMoveFirst);
    });

    [Fact]
    public void Download_next_puts_a_download_before_the_others_that_wait() => With(model =>
    {
        var a = Add(model, "A");
        Add(model, "B");
        var c = Add(model, "C");

        c.MoveFirstCommand.Execute(null);

        Assert.Equal(["A:Running:0", "C:Waiting:1", "B:Waiting:2"], Line(model));

        Remove(model, a);

        Assert.Equal(["C:Running:0", "B:Waiting:1"], Line(model));
    });

    [Fact]
    public void A_download_with_a_problem_stays_for_another_try_and_the_next_one_runs() => With(model =>
    {
        var a = Add(model, "A");
        Add(model, "B");

        a.Fail("The server answered 403.");
        model.PumpDownloads();

        Assert.Equal(["A:Failed:0", "B:Running:0"], Line(model));
        Assert.Equal("The server answered 403.", a.Detail);
        Assert.Equal("bad", a.Tone);
        Assert.Equal("Try again", a.ContinueTip);
        Assert.False(a.WaitForTurnAsync().IsCompleted);

        a.ContinueCommand.Execute(null);

        Assert.Equal(["A:Waiting:1", "B:Running:0"], Line(model));
        Assert.Equal("", a.Tone);
    });

    [Fact]
    public void Taking_a_download_out_ends_its_wait_or_its_transfer() => With(model =>
    {
        var a = Add(model, "A");
        var b = Add(model, "B");
        var wait = b.WaitForTurnAsync();

        b.CancelCommand.Execute(null);
        a.CancelCommand.Execute(null);

        Assert.True(wait.IsCanceled);
        Assert.True(b.Token.IsCancellationRequested);
        Assert.True(a.TransferToken.IsCancellationRequested);
    });
}
