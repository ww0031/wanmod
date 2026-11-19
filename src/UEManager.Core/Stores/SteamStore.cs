using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UEManager.Core.Mods;

namespace UEManager.Core.Stores;

/// <summary>
/// Finds a game in the Steam store by its name, for the pictures of a game that has none of its own: one from Epic, one
/// added by its folder, or a Steam demo without a cover. Only a store name equal to the game's counts, so a game never
/// takes another one's picture.
/// </summary>
public static partial class SteamStore
{
    private const string SearchUrl = "https://store.steampowered.com/api/storesearch/?l=english&cc=US&term=";

    private const string ItemsUrl = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=";

    /// <summary>How many apps one request names.</summary>
    private const int NamesPerRequest = 100;

    /// <summary>The store's address of an app's tall library cover.</summary>
    public static string CoverUrl(string appId) => $"https://shared.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg";

    /// <summary>The store's address of an app's wide library background.</summary>
    public static string HeroUrl(string appId) => $"https://shared.steamstatic.com/store_item_assets/steam/apps/{appId}/library_hero.jpg";

    /// <summary>
    /// Steam's name of one of the app's languages and the country its store answers for, as "schinese" and "CN" for
    /// "zh-CN". Null for English, the language the games' names are in already.
    /// </summary>
    public static (string Language, string Country)? LanguageOf(string appLanguage) => appLanguage switch
    {
        "zh-CN" => ("schinese", "CN"),
        "zh-TW" => ("tchinese", "TW"),
        "vi" => ("vietnamese", "VN"),
        _ => null,
    };

    /// <summary>
    /// The names the store gives apps in a language, by app id: 艾尔登法环 for 1245620 in "schinese". An app without a
    /// name in the language comes with its English one; an app the store does not know is left out.
    /// </summary>
    /// <exception cref="ModException">The store could not be reached.</exception>
    public static async Task<IReadOnlyDictionary<string, string>> NamesAsync(HttpClient http, IEnumerable<string> appIds, string language,
        string country, CancellationToken cancellation = default)
    {
        var names = new Dictionary<string, string>();
        var ids = appIds.Where(id => id.Length is > 0 and < 12 && id.All(char.IsAsciiDigit)).Distinct();
        foreach (var batch in ids.Chunk(NamesPerRequest))
        {
            var input = JsonSerializer.Serialize(new
            {
                ids = batch.Select(id => new { appid = long.Parse(id, CultureInfo.InvariantCulture) }),
                context = new { language, country_code = country },
                data_request = new { include_basic_info = true },
            });
            foreach (var (id, name) in ReadNames(await GetAsync(http, ItemsUrl + Uri.EscapeDataString(input), cancellation)))
                names[id] = name;
        }
        return names;
    }

    /// <summary>The app ids and names in the store's answer to <see cref="NamesAsync"/>.</summary>
    internal static IReadOnlyDictionary<string, string> ReadNames(string json)
    {
        var names = new Dictionary<string, string>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("response", out var response)
                || !response.TryGetProperty("store_items", out var items) || items.ValueKind != JsonValueKind.Array)
                return names;
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("appid", out var id) && id.ValueKind == JsonValueKind.Number && id.GetRawText() != "0"
                    && item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } text)
                    names[id.GetRawText()] = text;
            }
        }
        catch (JsonException)
        {
        }
        return names;
    }

    /// <summary>The app id of the game with one of these names; a demo's name is also tried without "Demo". Null when none matches.</summary>
    /// <param name="except">An app id that does not count, the game's own when its pictures are missing.</param>
    /// <exception cref="ModException">The store could not be reached or gave an answer the app cannot read.</exception>
    public static async Task<string?> FindAppIdAsync(HttpClient http, IEnumerable<string> names, string? except = null, CancellationToken cancellation = default)
    {
        foreach (var name in SearchNames(names))
        {
            var json = await GetAsync(http, SearchUrl + Uri.EscapeDataString(name), cancellation);
            if (PickAppId(json, name, except) is { } appId)
                return appId;
        }
        return null;
    }

    /// <summary>The names to search for, in their order: each without ™ and ®, then without a "Demo" or "Playtest" at its end.</summary>
    internal static IReadOnlyList<string> SearchNames(IEnumerable<string> names)
    {
        var clean = names.Select(n => Marks().Replace(n, "").Trim()).Where(n => Key(n).Length > 0).ToList();
        return clean.Concat(clean.Select(n => DemoSuffix().Replace(n, "").Trim()).Where(n => Key(n).Length > 0))
            .DistinctBy(Key).ToList();
    }

    /// <summary>The id of the first app in a store search whose name equals <paramref name="name"/>, apart from case and signs.</summary>
    internal static string? PickAppId(string json, string name, string? except = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return null;
            var wanted = Key(name);
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var found) && found.ValueKind == JsonValueKind.String && Key(found.GetString()!) == wanted
                    && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
                    && id.GetRawText() is var appId && appId != except)
                    return appId;
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A name with only its letters and digits, in lower case, so "Hades II" matches "HADES II™".</summary>
    private static string Key(string name) => new(Marks().Replace(name, "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static async Task<string> GetAsync(HttpClient http, string url, CancellationToken cancellation)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await http.GetAsync(url, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new ModException($"The Steam store answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            return await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            throw new ModException($"The Steam store could not be reached: {ex.Message}");
        }
    }

    [GeneratedRegex("[™®©]")]
    private static partial Regex Marks();

    [GeneratedRegex(@"[\s:\-–]*\b(demo|playtest)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DemoSuffix();
}
