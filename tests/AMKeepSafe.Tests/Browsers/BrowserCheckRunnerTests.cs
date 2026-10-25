using System.Text.Json.Nodes;
using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Browsers;

public sealed class BrowserCheckRunnerTests : IDisposable
{
    private readonly BrowserFolders _folders = new();
    private readonly FakeRegistry _registry = new();
    private readonly MemoryLog _log = new();

    public void Dispose() => _folders.Dispose();

    private BrowserCheckRunner Runner(PcContext? context = null) => BrowserKit.Runner(_folders.Locations, _registry, context, _log);

    [Fact]
    public async Task No_browsers_used_on_this_account_says_so()
    {
        var report = await Runner().RunAsync();

        Assert.True(report.NoBrowsers);
        Assert.Equal(BrowserKit.Text.Notes.NoBrowsers, report.Note);
        Assert.Equal(BrowserSummaryCheck.NoBrowsers, BrowserSummaryCheck.Evaluate(report).Code);
    }

    [Fact]
    public async Task Each_profile_gets_a_section_and_settings_on_this_PC_appear_once_per_browser()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", new JsonObject(), friendlyName: "Pat");
        _folders.ChromiumProfile(Browser.Chrome, "Profile 1", new JsonObject(), friendlyName: "Work");
        _folders.ChromiumProfile(Browser.Edge, "Default", new JsonObject());

        var report = await Runner().RunAsync();

        Assert.Equal(["Microsoft Edge", "Google Chrome: Pat", "Google Chrome: Work"], report.Sections.Select(s => s.Label));
        Assert.Equal(["edge-managed", "edge-default-search", "edge-default-start-pages", "edge-default-extensions", "edge-default-notifications"], report.Sections[0].Results.Select(r => r.CheckId));
        Assert.Equal(1, report.AllResults.Count(r => r.CheckId == "chrome-managed"));
        Assert.Contains(report.Sections[2].Results, r => r.CheckId == "chrome-profile-1-search");
    }

    [Fact]
    public async Task Wording_names_the_browser_and_buttons_point_at_that_browser()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", JsonNode.Parse("""
            { "default_search_provider_data": { "template_url_data": { "short_name": "Super Search", "url": "https://supersearch.example/?q={searchTerms}" } },
              "profile": { "content_settings": { "exceptions": { "notifications": { "https://alerts.example:443,*": { "setting": 1 } } } } } }
            """)!.AsObject());

        var report = await Runner().RunAsync();
        var search = report.AllResults.Single(r => r.CheckId == "chrome-default-search");
        var notifications = report.AllResults.Single(r => r.CheckId == "chrome-default-notifications");

        Assert.Equal(CheckStatus.Attention, search.Status);
        Assert.StartsWith("Google Chrome searches with Super Search", search.Summary);
        Assert.All(search.Actions, a => Assert.Equal(Browser.Chrome, a.Browser));
        Assert.Equal(BrowserPage.Search, search.Actions[0].BrowserPage);
        Assert.Equal(["https://alerts.example"], notifications.Items);
        Assert.Equal(BrowserSummaryCheck.SomethingToLook, BrowserSummaryCheck.Evaluate(report).Code);
    }

    [Fact]
    public async Task Policies_show_up_as_managed_and_as_the_reason_for_the_search_engine()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", new JsonObject());
        _registry.Set(RegistryRoot.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "DefaultSearchProviderSearchURL", "https://hijack.example/?q={searchTerms}");

        var report = await Runner().RunAsync();

        Assert.Equal(BrowserFindings.Codes.Present, report.AllResults.Single(r => r.CheckId == "chrome-managed").OutcomeCode);
        Assert.Equal(BrowserFindings.Codes.SetByPolicy, report.AllResults.Single(r => r.CheckId == "chrome-default-search").OutcomeCode);
    }

    [Fact]
    public async Task On_a_work_PC_policies_are_information_only()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", new JsonObject());
        _registry.Set(RegistryRoot.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "HomepageLocation", "https://intranet.example");

        var report = await Runner(new PcContext(true, @"CORP\pat", @"CORP\pat")).RunAsync();

        Assert.Equal(CheckStatus.Info, report.AllResults.Single(r => r.CheckId == "chrome-managed").Status);
    }

    [Fact]
    public async Task Says_so_when_a_different_accounts_browsers_were_checked()
    {
        var report = await Runner(new PcContext(false, @"PC\Admin", @"PC\Pat")).RunAsync();

        Assert.Contains(@"PC\Admin", report.Note);
        Assert.Contains(@"PC\Pat", report.Note);
        Assert.True(_log.Has(LogLevel.Warning, "browsers were checked"));
    }

    [Fact]
    public async Task A_healthy_browser_is_all_clear()
    {
        _folders.ChromiumProfile(Browser.Edge, "Default", new JsonObject());

        var report = await Runner().RunAsync();

        Assert.DoesNotContain(report.AllResults, r => r.Status.NeedsAction());
        Assert.Equal(BrowserSummaryCheck.AllClear, BrowserSummaryCheck.Evaluate(report).Code);
    }

    [Fact]
    public async Task A_profile_that_cant_be_read_shows_as_couldnt_check()
    {
        var folder = _folders.ChromiumProfile(Browser.Chrome, "Default", new JsonObject());
        File.WriteAllText(Path.Combine(folder, "Preferences"), "{ broken");

        var report = await Runner().RunAsync();

        Assert.Equal(CheckStatus.Unknown, report.AllResults.Single(r => r.CheckId == "chrome-default-unreadable").Status);
    }

    [Fact]
    public async Task Keeps_the_last_report_and_tells_listeners()
    {
        var runner = Runner();
        BrowserCheckReport? heard = null;
        runner.Finished += r => heard = r;

        var report = await runner.RunAsync();

        Assert.Same(report, runner.LastReport);
        Assert.Same(report, heard);
    }

    [Fact]
    public async Task Every_finding_is_logged()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", new JsonObject());

        await Runner().RunAsync();

        Assert.True(_log.Has(LogLevel.Info, "Browser check 'chrome-default-search': Good"));
        Assert.True(_log.Has(LogLevel.Info, "Browser check finished"));
    }

    [Theory]
    [InlineData(@"PC\Pat", @"PC\Pat", false)]
    [InlineData(@"PC\Pat", @"pc\pat", false)]
    [InlineData(@"PC\Admin", @"PC\Pat", true)]
    [InlineData(@"PC\Admin", null, false)]
    public void Knows_when_the_app_runs_as_a_different_account(string runningAs, string? signedIn, bool different)
    {
        Assert.Equal(different, new PcContext(false, runningAs, signedIn).IsDifferentAccount);
    }

    [Fact]
    public void Reads_whether_this_is_a_work_PC()
    {
        Assert.True(AMKeepSafe.Core.Checks.Parsing.FactParsers.PcContext("""{"partOfDomain":false,"userName":"PC\\Pat","azureAdJoined":true,"mdmEnrolled":false}""", @"PC\Pat").IsWorkOrSchoolPc);
        var home = AMKeepSafe.Core.Checks.Parsing.FactParsers.PcContext("""{"partOfDomain":false,"userName":"PC\\Pat","azureAdJoined":false,"mdmEnrolled":false}""", @"PC\Pat");
        Assert.False(home.IsWorkOrSchoolPc);
        Assert.Equal(@"PC\Pat", home.SignedInUser);
    }
}
