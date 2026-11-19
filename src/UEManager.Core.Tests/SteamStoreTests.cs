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

    [Fact]
    public void The_store_names_the_games_in_the_app_s_language()
    {
        // As the store answers for "schinese": an app it does not know comes as appid 0 without a name.
        var names = SteamStore.ReadNames("""
            {"response":{"store_items":[
              {"item_type":0,"id":1245620,"success":1,"visible":true,"name":"艾尔登法环","appid":1245620},
              {"item_type":0,"id":570,"success":1,"visible":true,"name":"Dota 2","appid":570},
              {"appid":0,"success":15,"visible":false}]}}
            """);

        Assert.Equal(2, names.Count);
        Assert.Equal("艾尔登法环", names["1245620"]);
        Assert.Equal("Dota 2", names["570"]);
        Assert.Empty(SteamStore.ReadNames("<html>"));
        Assert.Empty(SteamStore.ReadNames("""{"response":{}}"""));
        Assert.Equal(("schinese", "CN"), SteamStore.LanguageOf("zh-CN"));
        Assert.Equal(("tchinese", "TW"), SteamStore.LanguageOf("zh-TW"));
        Assert.Null(SteamStore.LanguageOf("en"));
    }
}
