using System.IO;
using UEManager.Core.Games;
using UEManager.UI.Services;
using Xunit;

namespace UEManager.UI.Tests;

public class HelpReportTests
{
    [Fact]
    public void The_report_names_where_the_remote_data_and_the_update_check_came_from_but_no_address()
    {
        var facts = new HelpFacts(GameProfile.BuiltIn().First(p => p.Id == "phantom-blade-zero"), null, null, null, [], [], null,
            Path.Combine(Path.GetTempPath(), "uemm-no-logs-" + Guid.NewGuid().ToString("N")),
            RemoteData: "Cloudflare", UpdateCheck: "Cloudflare, up to date (newest 1.5.2)");

        var report = HelpReport.Collect(facts);

        Assert.Contains("Remote data: Cloudflare; update check: Cloudflare, up to date (newest 1.5.2)", report);
        Assert.DoesNotContain("openmods.nexus", report);
        Assert.DoesNotContain("pages.dev", report);
    }

    [Fact]
    public void The_issue_page_takes_the_first_line_as_its_title_and_says_to_paste()
    {
        var url = HelpReport.IssueUrl("  发型模组启用不了 & 只显示图标\r\n第二行  ", "Press Ctrl+V here");

        Assert.StartsWith("https://github.com/ww0031/wanmod/issues/new?title=", url);
        var query = new Uri(url).Query[1..].Split('&').Select(part => part.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Assert.Equal("发型模组启用不了 & 只显示图标", query["title"]);
        Assert.Equal("Press Ctrl+V here", query["body"]);
    }

    [Fact]
    public void A_long_first_line_is_cut_for_the_title_and_never_inside_a_character()
    {
        var url = HelpReport.IssueUrl(new string('a', 59) + "😀 and much more text that follows", "paste");

        Assert.Contains("title=" + new string('a', 59) + Uri.EscapeDataString("…") + "&", url);
    }

    [Fact]
    public void No_words_give_an_empty_title()
    {
        Assert.Contains("issues/new?title=&body=paste", HelpReport.IssueUrl("  ", "paste"));
    }
}
