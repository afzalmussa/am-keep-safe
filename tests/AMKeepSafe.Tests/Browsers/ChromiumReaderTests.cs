using System.Text.Json.Nodes;
using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Browsers;

public sealed class ChromiumReaderTests : IDisposable
{
    private const string StoreId = "cjpalhdlnbpafiamejdnhcphjbkeiagm";
    private const string UnpackedId = "aaaabbbbccccddddeeeeffffgggghhhh";
    private const string PolicyId = "ppppoooolllliiiiccccyyyyaaaabbbb";

    private static readonly HashSet<string> NoForced = [];

    private readonly BrowserFolders _folders = new();

    public void Dispose() => _folders.Dispose();

    private ChromiumReader Reader => new(_folders.Locations);

    private static JsonObject Prefs(string json = "{}") => JsonNode.Parse(json)!.AsObject();

    private BrowserProfile ReadOne(Browser browser = Browser.Chrome, PolicyOverrides? policy = null, IReadOnlySet<string>? forced = null) =>
        Assert.Single(Reader.ReadProfiles(browser, policy ?? PolicyOverrides.None, forced ?? NoForced));

    [Fact]
    public void A_browser_never_used_on_this_account_has_no_profiles()
    {
        Assert.False(Reader.IsInstalledForThisAccount(Browser.Chrome));
        Assert.Empty(Reader.ReadProfiles(Browser.Chrome, PolicyOverrides.None, NoForced));
    }

    [Fact]
    public void Reads_every_profile_with_its_friendly_name_default_first()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Profile 1", Prefs(), friendlyName: "Work");
        _folders.ChromiumProfile(Browser.Chrome, "Default", Prefs(), friendlyName: "Pat");

        var profiles = Reader.ReadProfiles(Browser.Chrome, PolicyOverrides.None, NoForced);

        Assert.Equal(["Default", "Profile 1"], profiles.Select(p => p.ProfileKey));
        Assert.Equal(["Pat", "Work"], profiles.Select(p => p.ProfileName));
    }

    [Theory]
    [InlineData(Browser.Chrome, "Google")]
    [InlineData(Browser.Edge, "Bing")]
    public void An_untouched_profile_uses_the_browsers_normal_search_engine_and_start_page(Browser browser, string engine)
    {
        _folders.ChromiumProfile(browser, "Default", Prefs());

        var profile = ReadOne(browser);

        Assert.Equal(new SearchSetting(engine, null, SetBy.BrowserDefault), profile.Search);
        Assert.Equal(SetBy.BrowserDefault, profile.StartPages.SetBy);
        Assert.Empty(profile.StartPages.Pages);
        Assert.Empty(profile.Extensions);
        Assert.Empty(profile.NotificationSites);
        Assert.Null(profile.ReadProblem);
    }

    [Fact]
    public void Reads_a_search_engine_the_person_chose()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", Prefs("""
            { "default_search_provider_data": { "template_url_data": { "short_name": "DuckDuckGo", "url": "https://duckduckgo.com/?q={searchTerms}" } } }
            """));

        Assert.Equal(new SearchSetting("DuckDuckGo", "https://duckduckgo.com/?q={searchTerms}", SetBy.User), ReadOne().Search);
    }

    [Fact]
    public void Reads_start_pages_and_home_page()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", Prefs("""
            { "session": { "restore_on_startup": 4, "startup_urls": [ "https://news.example", "https://mail.example" ] },
              "homepage_is_newtabpage": false, "homepage": "https://home.example" }
            """));

        var pages = ReadOne().StartPages;

        Assert.Equal(SetBy.User, pages.SetBy);
        Assert.Equal(["https://news.example", "https://mail.example", "https://home.example"], pages.Pages);
    }

    [Fact]
    public void Restoring_the_last_session_isnt_a_start_page()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", Prefs("""{ "session": { "restore_on_startup": 1, "startup_urls": [ "https://ignored.example" ] } }"""));

        Assert.Empty(ReadOne().StartPages.Pages);
    }

    [Fact]
    public void Lists_only_sites_allowed_to_send_notifications()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", Prefs("""
            { "profile": { "content_settings": { "exceptions": { "notifications": {
                "https://news.example:443,*": { "setting": 1 },
                "https://blocked.example:443,*": { "setting": 2 },
                "https://virus-alert.example:443,*": { "setting": 1 }
            } } } } }
            """));

        Assert.Equal(["https://news.example", "https://virus-alert.example"], ReadOne().NotificationSites);
    }

    [Fact]
    public void Works_out_where_each_add_on_came_from()
    {
        var folder = _folders.ChromiumProfile(Browser.Chrome, "Default", Prefs(), Prefs($$"""
            { "extensions": { "settings": {
                "{{StoreId}}": { "location": 1, "from_webstore": true, "state": 1, "manifest": { "name": "Google Docs Offline" } },
                "{{UnpackedId}}": { "location": 4, "path": "C:\\Users\\Pat\\Downloads\\helper", "manifest": { "name": "Shopping Helper" } },
                "{{PolicyId}}": { "location": 7, "manifest": { "name": "Coupon Finder", "update_url": "https://clients2.google.com/service/update2/crx" } },
                "mhjfbmdgcfjbbpaeojofohoefgiehjai": { "location": 5, "manifest": { "name": "Chrome PDF Viewer" } },
                "themethemethemethemethemethemethe": { "location": 1, "from_webstore": true, "manifest": { "name": "Ocean", "theme": {} } }
            } } }
            """));

        var extensions = ReadOne().Extensions;

        Assert.Equal(["Coupon Finder", "Google Docs Offline", "Shopping Helper"], extensions.Select(e => e.Name));
        Assert.Equal(ExtensionSource.Policy, extensions.Single(e => e.Id == PolicyId).Source);
        Assert.Equal(ExtensionSource.Store, extensions.Single(e => e.Id == StoreId).Source);
        Assert.Equal(ExtensionSource.OutsideStore, extensions.Single(e => e.Id == UnpackedId).Source);
        Assert.NotNull(folder);
    }

    [Fact]
    public void An_add_on_a_policy_forces_on_counts_as_policy_whatever_its_location_says()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", Prefs(), Prefs($$"""
            { "extensions": { "settings": { "{{StoreId}}": { "location": 1, "from_webstore": true, "manifest": { "name": "Docs" } } } } }
            """));

        var extension = Assert.Single(ReadOne(forced: new HashSet<string> { StoreId }).Extensions);

        Assert.Equal(ExtensionSource.Policy, extension.Source);
    }

    [Fact]
    public void Reads_names_from_the_add_ons_own_files_including_translated_names()
    {
        var folder = _folders.ChromiumProfile(Browser.Edge, "Default", Prefs(), Prefs($$"""
            { "extensions": { "settings": { "{{StoreId}}": { "location": 1, "path": "{{StoreId}}\\1.0.0_0", "state": 1 } } } }
            """));
        _folders.ChromiumExtensionFiles(folder, StoreId,
            JsonNode.Parse("""{ "name": "__MSG_appName__", "default_locale": "en", "update_url": "https://edge.microsoft.com/extensionwebstorebase/v1/crx" }""")!.AsObject(),
            """{ "appName": { "message": "Password Helper" } }""");

        var extension = Assert.Single(ReadOne(Browser.Edge).Extensions);

        Assert.Equal("Password Helper", extension.Name);
        Assert.Equal(ExtensionSource.Store, extension.Source);
    }

    [Theory]
    [InlineData("\"state\": 0", false)]
    [InlineData("\"state\": 1", true)]
    [InlineData("\"disable_reasons\": [ 1 ]", false)]
    [InlineData("\"disable_reasons\": []", true)]
    public void Knows_whether_an_add_on_is_switched_off(string fragment, bool enabled)
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", Prefs(), Prefs($$"""
            { "extensions": { "settings": { "{{StoreId}}": { "location": 1, "from_webstore": true, {{fragment}}, "manifest": { "name": "Docs" } } } } }
            """));

        Assert.Equal(enabled, Assert.Single(ReadOne().Extensions).Enabled);
    }

    [Fact]
    public void An_add_on_that_takes_over_search_and_start_pages_is_named()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", Prefs(), Prefs($$"""
            { "extensions": { "settings": { "{{UnpackedId}}": { "location": 1, "state": 1, "manifest": {
                "name": "Super Search",
                "chrome_settings_overrides": {
                    "search_provider": { "name": "SuperSearch", "search_url": "https://supersearch.example/?q={searchTerms}", "is_default": true },
                    "startup_pages": [ "https://supersearch.example/home" ]
                } } } } } }
            """));

        var profile = ReadOne();

        Assert.Equal(new SearchSetting("SuperSearch", "https://supersearch.example/?q={searchTerms}", SetBy.Extension, "Super Search"), profile.Search);
        Assert.Equal(SetBy.Extension, profile.StartPages.SetBy);
        Assert.Equal("Super Search", profile.StartPages.SetByName);
        Assert.Equal(["https://supersearch.example/home"], profile.StartPages.Pages);
    }

    [Fact]
    public void A_switched_off_add_on_doesnt_count_as_controlling_search()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", Prefs(), Prefs($$"""
            { "extensions": { "settings": { "{{UnpackedId}}": { "location": 1, "state": 0, "manifest": {
                "name": "Super Search", "chrome_settings_overrides": { "search_provider": { "name": "S", "search_url": "https://s.example/?q={searchTerms}" } } } } } } }
            """));

        Assert.Equal(SetBy.BrowserDefault, ReadOne().Search.SetBy);
    }

    [Fact]
    public void A_policy_beats_everything_else()
    {
        _folders.ChromiumProfile(Browser.Chrome, "Default", Prefs("""
            { "default_search_provider_data": { "template_url_data": { "short_name": "DuckDuckGo", "url": "https://duckduckgo.com/?q={searchTerms}" } } }
            """));

        var profile = ReadOne(policy: new PolicyOverrides("Forced", "https://forced.example/?q={searchTerms}", ["https://forced.example"]));

        Assert.Equal(SetBy.Policy, profile.Search.SetBy);
        Assert.Equal("https://forced.example/?q={searchTerms}", profile.Search.Url);
        Assert.Equal(new[] { "https://forced.example" }, profile.StartPages.Pages);
        Assert.Equal(SetBy.Policy, profile.StartPages.SetBy);
    }

    [Fact]
    public void A_damaged_profile_is_reported_not_crashed_on()
    {
        var folder = _folders.ChromiumProfile(Browser.Chrome, "Default", Prefs());
        File.WriteAllText(Path.Combine(folder, "Preferences"), "{ this is not json");

        Assert.NotNull(ReadOne().ReadProblem);
    }

    [Fact]
    public void Very_long_or_multi_line_add_on_names_are_tidied()
    {
        Assert.Equal("Two words", ChromiumReader.Tidy("  Two \n words "));
        Assert.Equal(83, ChromiumReader.Tidy(new string('x', 200))!.Length);
        Assert.Null(ChromiumReader.Tidy("   "));
    }

    [Fact]
    public void Reading_never_changes_the_profile_files()
    {
        var folder = _folders.ChromiumProfile(Browser.Chrome, "Default", Prefs("""{ "homepage": "https://home.example" }"""));
        var before = File.ReadAllBytes(Path.Combine(folder, "Preferences"));
        var written = File.GetLastWriteTimeUtc(Path.Combine(folder, "Preferences"));

        ReadOne();

        Assert.Equal(before, File.ReadAllBytes(Path.Combine(folder, "Preferences")));
        Assert.Equal(written, File.GetLastWriteTimeUtc(Path.Combine(folder, "Preferences")));
    }
}
