using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Browsers;

public sealed class FirefoxReaderTests : IDisposable
{
    private readonly BrowserFolders _folders = new();

    public void Dispose() => _folders.Dispose();

    private FirefoxReader Reader => new(_folders.Locations);

    private BrowserProfile ReadOne(PolicyOverrides? policy = null) => Assert.Single(Reader.ReadProfiles(policy ?? PolicyOverrides.None));

    private const string ExtensionsJson = """
        { "addons": [
            { "id": "uBlock0@raymondhill.net", "type": "extension", "location": "app-profile", "active": true,
              "defaultLocale": { "name": "uBlock Origin" }, "sourceURI": "https://addons.mozilla.org/firefox/downloads/file/1/ublock.xpi",
              "installTelemetryInfo": { "source": "amo" } },
            { "id": "helper@example.com", "type": "extension", "location": "app-system-share", "active": true,
              "defaultLocale": { "name": "PC Helper Toolbar" } },
            { "id": "coupons@example.com", "type": "extension", "location": "app-profile", "active": false,
              "defaultLocale": { "name": "Coupon Hunter" }, "installTelemetryInfo": { "source": "file-url" } },
            { "id": "work@example.com", "type": "extension", "location": "app-profile", "active": true,
              "defaultLocale": { "name": "Work Tools" }, "installTelemetryInfo": { "source": "enterprise-policy" } },
            { "id": "screenshots@mozilla.org", "type": "extension", "location": "app-builtin", "active": true, "defaultLocale": { "name": "Screenshots" } },
            { "id": "en-GB@dictionaries.addons.mozilla.org", "type": "dictionary", "location": "app-profile", "active": true, "defaultLocale": { "name": "English Dictionary" } }
        ] }
        """;

    [Fact]
    public void Not_used_on_this_account_means_no_profiles()
    {
        Assert.False(Reader.IsInstalledForThisAccount);
        Assert.Empty(Reader.ReadProfiles(PolicyOverrides.None));
    }

    [Fact]
    public void Reads_profiles_from_profiles_ini()
    {
        _folders.FirefoxProfile("abc.default-release", "default-release");
        _folders.FirefoxProfile("xyz.work", "Work");

        Assert.Equal(["default-release", "Work"], Reader.ReadProfiles(PolicyOverrides.None).Select(p => p.ProfileName));
    }

    [Fact]
    public void Lists_add_ons_with_where_they_came_from_and_skips_built_in_ones()
    {
        File.WriteAllText(Path.Combine(_folders.FirefoxProfile("abc.default", "default"), "extensions.json"), ExtensionsJson);

        var extensions = ReadOne().Extensions;

        Assert.Equal(["Coupon Hunter", "PC Helper Toolbar", "uBlock Origin", "Work Tools"], extensions.Select(e => e.Name));
        Assert.Equal(ExtensionSource.Store, extensions.Single(e => e.Name == "uBlock Origin").Source);
        Assert.Equal(ExtensionSource.OutsideStore, extensions.Single(e => e.Name == "PC Helper Toolbar").Source);
        Assert.Equal(ExtensionSource.OutsideStore, extensions.Single(e => e.Name == "Coupon Hunter").Source);
        Assert.Equal(ExtensionSource.Policy, extensions.Single(e => e.Name == "Work Tools").Source);
        Assert.False(extensions.Single(e => e.Name == "Coupon Hunter").Enabled);
    }

    [Fact]
    public void Reads_the_search_engine_from_the_compressed_search_file()
    {
        var folder = _folders.FirefoxProfile("abc.default", "default");
        BrowserFolders.FirefoxSearch(folder, """
            { "version": 9, "metaData": { "defaultEngineId": "ddg@search.mozilla.orgdefault" },
              "engines": [
                { "id": "google@search.mozilla.orgdefault", "_name": "Google", "_isAppProvided": true },
                { "id": "ddg@search.mozilla.orgdefault", "_name": "DuckDuckGo", "_isAppProvided": true,
                  "_extensionID": "ddg@search.mozilla.org", "_urls": [ { "template": "https://duckduckgo.com/" } ] }
              ] }
            """);

        Assert.Equal(new SearchSetting("DuckDuckGo", "https://duckduckgo.com/", SetBy.User), ReadOne().Search);
    }

    [Fact]
    public void Older_search_files_name_the_engine_instead()
    {
        var folder = _folders.FirefoxProfile("abc.default", "default");
        BrowserFolders.FirefoxSearch(folder, """{ "metaData": { "current": "Bing" }, "engines": [ { "_name": "Bing", "_urls": [ { "template": "https://www.bing.com/search" } ] } ] }""");

        Assert.Equal("Bing", ReadOne().Search.Name);
    }

    [Fact]
    public void An_add_on_that_took_over_search_is_named()
    {
        var folder = _folders.FirefoxProfile("abc.default", "default");
        File.WriteAllText(Path.Combine(folder, "extensions.json"), """
            { "addons": [ { "id": "search@hijack.example", "type": "extension", "location": "app-profile", "active": true, "defaultLocale": { "name": "Search Pro Plus" } } ] }
            """);
        File.WriteAllText(Path.Combine(folder, "extension-settings.json"), """
            { "version": 2, "default_search": { "defaultSearch": { "precedenceList": [ { "id": "search@hijack.example", "value": "Hijack", "enabled": true } ] } } }
            """);
        BrowserFolders.FirefoxSearch(folder, """
            { "metaData": { "defaultEngineId": "hijack" }, "engines": [ { "id": "hijack", "_name": "Hijack Search", "_extensionID": "search@hijack.example", "_urls": [ { "template": "https://hijack.example/" } ] } ] }
            """);

        Assert.Equal(new SearchSetting("Hijack Search", "https://hijack.example/", SetBy.Extension, "Search Pro Plus"), ReadOne().Search);
    }

    [Fact]
    public void Reads_the_home_page_from_prefs()
    {
        var folder = _folders.FirefoxProfile("abc.default", "default");
        File.WriteAllText(Path.Combine(folder, "prefs.js"), """
            // Mozilla User Preferences
            user_pref("browser.startup.page", 1);
            user_pref("browser.startup.homepage", "https://news.example|https://mail.example|about:home");
            """);

        var pages = ReadOne().StartPages;

        Assert.Equal(SetBy.User, pages.SetBy);
        Assert.Equal(["https://news.example", "https://mail.example"], pages.Pages);
    }

    [Fact]
    public void An_add_on_that_took_over_the_home_page_is_named()
    {
        var folder = _folders.FirefoxProfile("abc.default", "default");
        File.WriteAllText(Path.Combine(folder, "extensions.json"), """
            { "addons": [ { "id": "home@hijack.example", "type": "extension", "location": "app-profile", "active": true, "defaultLocale": { "name": "New Tab Deals" } } ] }
            """);
        File.WriteAllText(Path.Combine(folder, "extension-settings.json"), """
            { "prefs": { "homepage_override": { "precedenceList": [ { "id": "home@hijack.example", "value": "https://deals.example", "enabled": true } ] } } }
            """);

        var pages = ReadOne().StartPages;

        Assert.Equal(SetBy.Extension, pages.SetBy);
        Assert.Equal("New Tab Deals", pages.SetByName);
        Assert.Equal(["https://deals.example"], pages.Pages);
    }

    [Fact]
    public void Reads_sites_allowed_to_send_notifications_from_the_database()
    {
        var folder = _folders.FirefoxProfile("abc.default", "default");
        BrowserFolders.FirefoxNotifications(folder, ("https://news.example", 1), ("https://blocked.example", 2), ("https://virus-alert.example", 1));

        Assert.Equal(["https://news.example", "https://virus-alert.example"], ReadOne().NotificationSites);
    }

    [Fact]
    public void Reading_the_database_leaves_Firefoxs_own_copy_untouched()
    {
        var folder = _folders.FirefoxProfile("abc.default", "default");
        BrowserFolders.FirefoxNotifications(folder, ("https://news.example", 1));
        var database = Path.Combine(folder, "permissions.sqlite");
        var before = File.ReadAllBytes(database);

        ReadOne();

        Assert.Equal(before, File.ReadAllBytes(database));
        Assert.False(File.Exists(database + "-journal"));
    }

    [Fact]
    public void A_policy_beats_the_profile()
    {
        _folders.FirefoxProfile("abc.default", "default");

        var profile = ReadOne(new PolicyOverrides("Forced Search", null, ["https://forced.example"]));

        Assert.Equal(SetBy.Policy, profile.Search.SetBy);
        Assert.Equal("Forced Search", profile.Search.Name);
        Assert.Equal(SetBy.Policy, profile.StartPages.SetBy);
    }

    [Fact]
    public void A_damaged_search_file_is_reported_not_crashed_on()
    {
        var folder = _folders.FirefoxProfile("abc.default", "default");
        File.WriteAllBytes(Path.Combine(folder, "search.json.mozlz4"), [1, 2, 3]);

        Assert.NotNull(ReadOne().ReadProblem);
    }

    [Fact]
    public void Prefs_values_with_quotes_and_backslashes_are_read_correctly()
    {
        var path = Path.Combine(_folders.FirefoxProfile("abc.default", "default"), "prefs.js");
        File.WriteAllText(path, """user_pref("some.text", "a \"quoted\" C:\\path");""" + "\nuser_pref(\"some.number\", 42);");

        var prefs = FirefoxReader.ReadPrefs(path);

        Assert.Equal("a \"quoted\" C:\\path", prefs["some.text"]);
        Assert.Equal("42", prefs["some.number"]);
    }
}
