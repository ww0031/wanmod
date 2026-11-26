using System.Net;
using System.Text;
using UEManager.Core.Mods;

namespace UEManager.Core.Tests;

/// <summary>The free translation of mod texts: the service's answers, the size of each request, and the saved translations.</summary>
public class FreeTranslatorTests
{
    [Fact]
    public void An_answer_has_one_text_or_several()
    {
        Assert.Equal(["地图收藏品标记"], FreeTranslator.ReadAnswer("""["地图收藏品标记","en"]""", 1));
        Assert.Equal(["一", "二"], FreeTranslator.ReadAnswer("""[["一","en"],["二","en"]]""", 2));
        Assert.Null(FreeTranslator.ReadAnswer("""[["一","en"]]""", 2));
        Assert.Null(FreeTranslator.ReadAnswer("<html>", 1));
    }

    [Fact]
    public void Requests_stay_short_and_keep_the_order()
    {
        string[] texts = [new('a', 3000), new('b', 900), new('c', 200), new('d', 5000)];

        var batches = FreeTranslator.Batches(texts).ToList();

        Assert.Equal([[texts[0], texts[1]], [texts[2]], [texts[3]]], batches);
    }

    [Theory]
    [InlineData("Map Collectible Markers", "zh-CN", true)]
    [InlineData("明末无常女音", "zh-CN", false)]
    [InlineData("女声", "en", true)]
    [InlineData("female voice", "en", false)]
    public void Only_a_text_in_another_language_is_sent(string text, string language, bool sent) =>
        Assert.Equal(sent, FreeTranslator.NeedsTranslation(text, language));

    [Theory]
    [InlineData("UE4SS 幻影之刃零", "zh-CN", "UE4SS 影之刃零")]
    [InlineData("幻影零式 - 更好的 HUD", "zh-CN", "影之刃零 - 更好的 HUD")]
    [InlineData("在《幻刃零》中可以闪避。", "zh-TW", "在《影之刃零》中可以闪避。")]
    [InlineData("适用于《Phantom Blade Zero》的 UE4SS", "zh-CN", "适用于《影之刃零》的 UE4SS")]
    [InlineData("UE4SS cho Phantom Blade Zero", "vi", "UE4SS cho Phantom Blade Zero")]
    public void The_game_keeps_its_Chinese_name(string translated, string language, string shown) =>
        Assert.Equal(shown, FreeTranslator.GameName(translated, language));

    [Fact]
    public async Task Each_text_is_sent_once_and_Chinese_stays_as_it_is()
    {
        using var folder = new TestFolder();
        var file = folder.Combine("translations.json");
        var service = new Service(HttpStatusCode.OK, """[["你好","en"],["世界","en"]]""");

        var first = await Google(service, file).TranslateAsync(["Hello", "已经是中文", "World"], "zh-CN");
        var again = await Google(service, file).TranslateAsync(["World", "Hello"], "zh-CN");

        Assert.Equal(["你好", "已经是中文", "世界"], first);
        Assert.Equal(["世界", "你好"], again);
        Assert.Equal(1, service.Requests);
        Assert.Equal("q=Hello&q=World", service.LastBody);
    }

    [Fact]
    public async Task An_empty_answer_shows_the_original_and_is_asked_again()
    {
        using var folder = new TestFolder();
        var file = folder.Combine("translations.json");
        var service = new Service(HttpStatusCode.OK, """[["","en"],["世界","en"]]""");

        var first = await Google(service, file).TranslateAsync(["Hello", "World"], "zh-TW");
        service.Answer = """["你好","en"]""";
        var again = await Google(service, file).TranslateAsync(["Hello", "World"], "zh-TW");

        Assert.Equal(["Hello", "世界"], first);
        Assert.Equal(["你好", "世界"], again);
        Assert.Equal(2, service.Requests);
        Assert.Equal("q=Hello", service.LastBody);
    }

    [Fact]
    public async Task A_refusal_is_an_error_and_nothing_is_kept()
    {
        using var folder = new TestFolder();
        var translator = new FreeTranslator(new HttpClient(new Service(HttpStatusCode.TooManyRequests, "")), folder.Combine("translations.json"));

        await Assert.ThrowsAsync<ModException>(() => translator.TranslateAsync(["Hello"], "zh-CN"));
    }

    [Fact]
    public void Tencent_answers_succ_with_the_texts_and_reports_a_failure_in_the_answer()
    {
        Assert.Equal(["一", "二"], FreeTranslator.ReadTencentAnswer("""{"header":{"ret_code":"succ"},"auto_translation":["一","二"]}""", 2));
        Assert.Null(FreeTranslator.ReadTencentAnswer("""{"header":{"ret_code":"Auth-Failed"},"message":"Rejected"}""", 1));
        Assert.Null(FreeTranslator.ReadTencentAnswer("""{"header":{"ret_code":"succ"},"auto_translation":["一"]}""", 2));
        Assert.Null(FreeTranslator.ReadTencentAnswer("""[["一","en"]]""", 1));
    }

    [Theory]
    [InlineData("zh-CN", "zh")]
    [InlineData("zh-TW", "zh-TW")]
    [InlineData("vi", "vi")]
    [InlineData("en-US", "en")]
    public void Tencent_has_its_own_language_names(string language, string tencent) =>
        Assert.Equal(tencent, FreeTranslator.TencentLanguage(language));

    [Fact]
    public void Automatic_asks_the_service_that_works_where_the_player_is_first()
    {
        Assert.Equal([TranslationService.Tencent, TranslationService.Google], FreeTranslator.Order(TranslationService.Auto, null, mainlandChina: true));
        Assert.Equal([TranslationService.Google, TranslationService.Tencent], FreeTranslator.Order(TranslationService.Auto, null, mainlandChina: false));
        Assert.Equal([TranslationService.Google, TranslationService.Tencent],
            FreeTranslator.Order(TranslationService.Auto, TranslationService.Google, mainlandChina: true));
        Assert.Equal([TranslationService.Tencent], FreeTranslator.Order(TranslationService.Tencent, TranslationService.Google, mainlandChina: false));
    }

    [Fact]
    public async Task When_one_service_fails_the_other_translates_and_is_kept_for_next_time()
    {
        using var folder = new TestFolder();
        var services = new ByHost(new()
        {
            ["transmart.qq.com"] = """{"header":{"ret_code":"Auth-Failed"}}""",
            ["clients5.google.com"] = """["你好","en"]""",
        });
        var translator = new FreeTranslator(new HttpClient(services), folder.Combine("translations.json"));

        var done = await translator.TranslateAsync(["Hello"], "zh-CN");

        Assert.Equal(["你好"], done);
        Assert.Equal(TranslationService.Google, translator.Used);
        Assert.Equal(TranslationService.Google, translator.First);
    }

    [Fact]
    public async Task Tencent_gets_the_texts_as_json()
    {
        using var folder = new TestFolder();
        var service = new Service(HttpStatusCode.OK, """{"header":{"ret_code":"succ"},"auto_translation":["你好","世界"]}""");
        var translator = new FreeTranslator(new HttpClient(service), folder.Combine("translations.json")) { Service = TranslationService.Tencent };

        var done = await translator.TranslateAsync(["Hello", "World"], "zh-CN");

        Assert.Equal(["你好", "世界"], done);
        Assert.Contains("\"text_list\":[\"Hello\",\"World\"]", service.LastBody);
        Assert.Contains("\"target\":{\"lang\":\"zh\"}", service.LastBody);
    }

    [Fact]
    public async Task A_category_the_services_get_wrong_alone_has_its_Chinese_name_and_is_not_sent()
    {
        using var folder = new TestFolder();
        var file = folder.Combine("translations.json");
        var service = new Service(HttpStatusCode.OK, """["你好","en"]""");

        var simplified = await Google(service, file).TranslateAsync(["Characters", "Hello", "Utilities"], "zh-CN");
        var traditional = await Google(service, file).TranslateAsync(["Utilities", "Save Games"], "zh-TW");

        Assert.Equal(["角色", "你好", "实用工具"], simplified);
        Assert.Equal(["實用工具", "存檔"], traditional);
        Assert.Equal(1, service.Requests);
        Assert.Equal("q=Hello", service.LastBody);
        Assert.Null(FreeTranslator.SiteWord("Characters", "vi"));
        Assert.Null(FreeTranslator.SiteWord("New Characters", "zh-CN"));
    }

    private static FreeTranslator Google(HttpMessageHandler service, string file) =>
        new(new HttpClient(service), file) { Service = TranslationService.Google };

    private sealed class ByHost(Dictionary<string, string> answers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(answers[request.RequestUri!.Host], Encoding.UTF8, "application/json"),
            });
    }

    private sealed class Service(HttpStatusCode status, string answer) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public string Answer { get; set; } = answer;

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(Answer, Encoding.UTF8, "application/json") };
        }
    }
}
