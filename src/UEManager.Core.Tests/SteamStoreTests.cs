using UEManager.Core.Stores;

namespace UEManager.Core.Tests;

/// <summary>Finding the Steam store's app for a game's pictures by its name.</summary>
public class SteamStoreTests
{
    private const string Search = """
        {"total":2,"items":[
          {"type":"app","name":"Sil and the Fading World","id":3054550},
          {"type":"app","name":"Sil and the Fading World Demo","id":3366110}]}
        """;

    [Theory]
    [InlineData("Sil and the Fading World", "3054550")]
    [InlineData("SIL AND THE FADING WORLD™", "3054550")]
    [InlineData("Sil and the Fading World Demo", "3366110")]
    [InlineData("Sil", null)]
    public void Only_the_same_name_counts(string name, string? appId) =>
        Assert.Equal(appId, SteamStore.PickAppId(Search, name));

    [Fact]
    public void The_game_itself_does_not_count()
    {
        Assert.Null(SteamStore.PickAppId(Search, "Sil and the Fading World Demo", except: "3366110"));
        Assert.Null(SteamStore.PickAppId("<html>", "Sil"));
        Assert.Null(SteamStore.PickAppId("""{"total":0}""", "Sil"));
    }

    [Fact]
    public void A_demo_is_also_searched_by_the_full_game_s_name()
    {
        Assert.Equal(["Sil and the Fading World Demo", "Hades II", "Sil and the Fading World"],
            SteamStore.SearchNames(["Sil and the Fading World Demo", "Hades II®", "  "]));
        Assert.Equal(["Hades"], SteamStore.SearchNames(["Hades", "HADES"]));
    }
}
