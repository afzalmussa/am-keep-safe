using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Tests.Support;
using Codes = AMKeepSafe.Core.Browsers.BrowserFindings.Codes;

namespace AMKeepSafe.Tests.Browsers;

public class BrowserFindingsTests
{
    private static readonly SearchEngineList Engines = BrowserKit.Engines;
    private static readonly BrowserFindings.ExtensionSettings ExtensionText = BrowserKit.Text.Catalog[BrowserFindings.Extensions].ParametersAs<BrowserFindings.ExtensionSettings>();
    private static readonly BrowserFindings.ManagedSettings ManagedText = BrowserKit.Text.Catalog[BrowserFindings.Managed].ParametersAs<BrowserFindings.ManagedSettings>();

    [Theory]
    [InlineData("Google", null, SetBy.BrowserDefault, Codes.BrowserDefault)]
    [InlineData("DuckDuckGo", "https://duckduckgo.com/?q={searchTerms}", SetBy.User, Codes.WellKnown)]
    [InlineData("Google", "{google:baseURL}search?q={searchTerms}", SetBy.User, Codes.WellKnown)]
    [InlineData("Super Search", "https://supersearch.example/?q={searchTerms}", SetBy.User, Codes.NotRecognised)]
    [InlineData("Bing", "https://www.bing.com/search?q={searchTerms}", SetBy.Extension, Codes.SetByExtension)]
    [InlineData("Google", "https://www.google.com/search?q={searchTerms}", SetBy.Policy, Codes.SetByPolicy)]
    [InlineData("Yahoo", "https://search.yahoo.com/yhs/search?hspart=partner&p={searchTerms}", SetBy.User, Codes.PartnerLink)]
    public void Search_engine(string name, string? url, SetBy setBy, string expected)
    {
        Assert.Equal(expected, BrowserFindings.EvaluateSearch(new SearchSetting(name, url, setBy, "An add-on"), Engines).Code);
    }

    [Fact]
    public void An_engine_we_dont_recognise_is_named_by_its_address_when_it_has_no_name()
    {
        var outcome = BrowserFindings.EvaluateSearch(new SearchSetting(null, "https://search.example.net/q?x={searchTerms}", SetBy.User), Engines);

        Assert.Equal("search.example.net", outcome.Values["engine"]);
    }

    [Theory]
    [InlineData(SetBy.BrowserDefault, 0, Codes.BrowserDefault)]
    [InlineData(SetBy.User, 2, Codes.UserPages)]
    [InlineData(SetBy.Extension, 1, Codes.SetByExtension)]
    [InlineData(SetBy.Policy, 1, Codes.SetByPolicy)]
    public void Start_pages(SetBy setBy, int pages, string expected)
    {
        var outcome = BrowserFindings.EvaluateStartPages(new StartPagesSetting(Enumerable.Range(1, pages).Select(i => $"https://p{i}.example").ToList(), setBy));

        Assert.Equal(expected, outcome.Code);
        Assert.Equal(pages, outcome.Items.Count);
    }

    [Fact]
    public void No_add_ons()
    {
        Assert.Equal(Codes.None, BrowserFindings.EvaluateExtensions([], BrowserKit.KnownBad, ExtensionText).Code);
    }

    [Fact]
    public void Add_ons_all_from_the_store_look_fine()
    {
        var outcome = BrowserFindings.EvaluateExtensions([new BrowserExtension("a", "Docs", ExtensionSource.Store, true)], BrowserKit.KnownBad, ExtensionText);

        Assert.Equal(Codes.AllFromStore, outcome.Code);
        Assert.Equal(["Docs"], outcome.Items);
    }

    [Fact]
    public void Add_ons_from_elsewhere_ask_do_you_recognise_it_and_come_first()
    {
        var outcome = BrowserFindings.EvaluateExtensions(
        [
            new BrowserExtension("a", "Docs", ExtensionSource.Store, true),
            new BrowserExtension("b", "Toolbar", ExtensionSource.OutsideStore, true),
            new BrowserExtension("c", "Coupons", ExtensionSource.Policy, false),
        ], BrowserKit.KnownBad, ExtensionText);

        Assert.Equal(Codes.SomeToCheck, outcome.Code);
        Assert.Equal(
            ["Coupons: put there by a setting on this PC (switched off)", "Toolbar: added outside the official store. Do you recognise it?", "Docs"],
            outcome.Items);
    }

    [Fact]
    public void A_known_harmful_add_on_is_red_and_listed_first()
    {
        var bad = BrowserKit.KnownBad.Extensions[0].Id;

        var outcome = BrowserFindings.EvaluateExtensions(
            [new BrowserExtension("a", "Docs", ExtensionSource.Store, true), new BrowserExtension(bad, "Weather Now", ExtensionSource.Store, true)],
            BrowserKit.KnownBad, ExtensionText);

        Assert.Equal(Codes.KnownBad, outcome.Code);
        Assert.Equal("Weather Now: removed from the store after it was found to be harmful", outcome.Items[0]);
    }

    [Fact]
    public void Notifications()
    {
        Assert.Equal(Codes.None, BrowserFindings.EvaluateNotifications([]).Code);
        var some = BrowserFindings.EvaluateNotifications(["https://news.example"]);
        Assert.Equal(Codes.Some, some.Code);
        Assert.Equal(["https://news.example"], some.Items);
    }

    [Fact]
    public void Policies_on_a_home_PC_are_worth_a_look_and_on_a_work_PC_are_information()
    {
        PolicyLocation[] policies = [new(PolicyPlace.Registry, RegistryRoot.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", ["HomepageLocation = https://x.example"])];

        Assert.Equal(Codes.None, BrowserFindings.EvaluateManaged([], false, ManagedText).Code);
        Assert.Equal(Codes.Present, BrowserFindings.EvaluateManaged(policies, false, ManagedText).Code);
        Assert.Equal(Codes.PresentWorkPc, BrowserFindings.EvaluateManaged(policies, true, ManagedText).Code);
    }

    [Fact]
    public void A_long_list_of_policies_is_shortened_with_a_count()
    {
        var lines = Enumerable.Range(1, 20).Select(i => $"Setting{i} = {i}").ToList();
        var outcome = BrowserFindings.EvaluateManaged([new PolicyLocation(PolicyPlace.Registry, RegistryRoot.LocalMachine, "k", lines)], false, ManagedText);

        Assert.Equal(13, outcome.Items.Count);
        Assert.Equal("and 8 more", outcome.Items[^1]);
    }

    [Fact]
    public void Every_finding_lists_the_outcomes_it_can_give()
    {
        Assert.Equal(6, BrowserFindings.OutcomeCodes.Count);
    }
}
