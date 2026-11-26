using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The box for adult content in Settings: turning it on asks first, turning it off does not.</summary>
[Collection("Language")]
public class AdultContentSettingTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-adult-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    /// <summary>Opens Settings on the settings an earlier start left; the check gets what is saved at any moment.</summary>
    private void Open(AppSettings? earlier, Action<MainViewModel, Func<AppSettings>> check) => UiThread.Run(() =>
    {
        var original = Localizer.Current.Language;
        try
        {
            Localizer.Current.SetLanguage(Localizer.English);
            var paths = new AppPaths(Path.Combine(folder, "Data"));
            paths.EnsureWritable();
            earlier?.Save(paths);
            var model = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Test" }, paths, new ArtSet(null, null));
            check(model, () => AppSettings.Load(paths));
        }
        finally
        {
            Localizer.Current.SetLanguage(original);
        }
    });

    [Fact]
    public void It_is_off_until_the_player_turns_it_on() => Open(null, (model, _) => Assert.False(model.ShowAdultContent));

    [Fact]
    public void Turning_it_on_asks_and_cancel_leaves_it_off() => Open(null, (model, saved) =>
    {
        var raised = 0;
        model.PropertyChanged += (_, e) => raised += e.PropertyName == nameof(MainViewModel.ShowAdultContent) ? 1 : 0;

        model.ShowAdultContent = true;

        Assert.Equal("Show adult content?", model.Confirm!.Title);
        Assert.Contains("18 or older", model.Confirm.Message);
        Assert.Contains("not in mainland China", model.Confirm.Message);
        Assert.Equal("I confirm, turn it on", model.Confirm.ConfirmText);
        Assert.False(model.ShowAdultContent);
        model.Confirm.CancelCommand.Execute(null);

        Assert.Null(model.Confirm);
        Assert.False(model.ShowAdultContent);
        Assert.False(saved().ShowAdultContent);
        Assert.Equal(1, raised);
    });

    [Fact]
    public void Confirming_turns_it_on_and_saves_it() => Open(null, (model, saved) =>
    {
        model.ShowAdultContent = true;
        model.Confirm!.AcceptCommand.Execute(null);

        Assert.Null(model.Confirm);
        Assert.True(model.ShowAdultContent);
        Assert.True(saved().ShowAdultContent);
    });

    [Fact]
    public void Turning_it_off_asks_nothing() => Open(new AppSettings { ShowAdultContent = true }, (model, saved) =>
    {
        Assert.True(model.ShowAdultContent);

        model.ShowAdultContent = false;

        Assert.Null(model.Confirm);
        Assert.False(model.ShowAdultContent);
        Assert.False(saved().ShowAdultContent);
    });
}
