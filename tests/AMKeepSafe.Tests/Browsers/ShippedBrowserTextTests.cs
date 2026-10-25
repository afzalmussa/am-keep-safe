using System.Text.RegularExpressions;
using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Browsers;

/// <summary>Checks on the real data/browser-check.json, search-engines.json and extensions-known-bad.json.</summary>
public partial class ShippedBrowserTextTests
{
    private static readonly BrowserText Text = BrowserKit.Text;

    public static TheoryData<string> Findings() => [.. BrowserFindings.OutcomeCodes.Keys];

    [Fact]
    public void Loads_with_no_problems()
    {
        Assert.Empty(Text.Catalog.Validate());
    }

    [Theory]
    [MemberData(nameof(Findings))]
    public void Every_outcome_has_wording_and_there_is_no_unused_wording(string finding)
    {
        Assert.Equal(BrowserFindings.OutcomeCodes[finding].Order(), Text.Catalog[finding].Outcomes.Keys.Order());
    }

    [Fact]
    public void Every_browser_has_a_name_and_every_page_in_its_own_address_space()
    {
        foreach (var browser in Enum.GetValues<Browser>())
        {
            Assert.False(string.IsNullOrWhiteSpace(Text.NameOf(browser)));
            foreach (var page in Enum.GetValues<BrowserPage>())
            {
                Assert.NotNull(Text.PageAddress(browser, page));
            }
        }
    }

    [Theory]
    [InlineData(Browser.Chrome, "edge://extensions")]
    [InlineData(Browser.Edge, "https://example.com")]
    [InlineData(Browser.Firefox, "file:///C:/Windows/System32/cmd.exe")]
    public void A_page_address_for_the_wrong_browser_or_outside_the_browser_is_refused(Browser browser, string address)
    {
        var text = new BrowserText
        {
            Catalog = Text.Catalog,
            Notes = Text.Notes,
            Browsers = new Dictionary<Browser, BrowserInfo> { [browser] = new() { Name = "X", Pages = new Dictionary<BrowserPage, string> { [BrowserPage.Extensions] = address } } },
        };

        Assert.Null(text.PageAddress(browser, BrowserPage.Extensions));
    }

    [Fact]
    public void Unknown_add_ons_are_never_called_harmful()
    {
        var someToCheck = Text.Catalog[BrowserFindings.Extensions].Outcomes[BrowserFindings.Codes.SomeToCheck];

        Assert.Equal(CheckStatus.Attention, someToCheck.Status);
        Assert.Contains("doesn't make them harmful", someToCheck.Summary);
        Assert.Contains("Do you recognise", someToCheck.Summary);
    }

    [Fact]
    public void Removing_policies_is_offered_only_on_a_home_PC()
    {
        var fixes = AMKeepSafe.Core.Fixes.FixCatalog.LoadEmbedded();

        foreach (var browser in new[] { "edge", "chrome", "firefox" })
        {
            Assert.Equal(["present"], fixes.For($"{browser}-managed", "present").Single().ForOutcomes);
            Assert.Empty(fixes.For($"{browser}-managed", "presentWorkPc"));
        }
    }

    [Fact]
    public void No_browser_wording_mentions_Pro_or_paying()
    {
        var texts = Text.Catalog.Checks.SelectMany(c => c.Outcomes.Values)
            .SelectMany(o => o.HowToFix.Prepend(o.Summary).Concat(o.Actions.Select(a => a.Label)));

        Assert.All(texts, t => Assert.False(ProOrPaying().IsMatch(t), $"Mentions Pro or paying: \"{t}\""));
    }

    [Fact]
    public void Every_known_bad_add_on_has_a_valid_id_and_a_source()
    {
        Assert.All(BrowserKit.KnownBad.Extensions, e =>
        {
            Assert.Matches("^[a-p]{32}$", e.Id);
            Assert.True(AMKeepSafe.Core.Configuration.AppSettings.IsUsableUrl(e.Source), $"{e.Id} has no https source");
        });
        Assert.Equal(BrowserKit.KnownBad.Extensions.Count, BrowserKit.KnownBad.Extensions.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void Every_search_engine_pattern_is_valid_and_anchored()
    {
        Assert.All(BrowserKit.Engines.Engines.SelectMany(e => e.HostPatterns), p =>
        {
            Assert.StartsWith("^", p);
            Assert.EndsWith("$", p);
            _ = new Regex(p);
        });
    }

    [GeneratedRegex(@"\bPro\b|\bupgrade\b|\bbuy\b|\bpurchase\b|\blicen[cs]e\b|£", RegexOptions.IgnoreCase)]
    private static partial Regex ProOrPaying();
}
