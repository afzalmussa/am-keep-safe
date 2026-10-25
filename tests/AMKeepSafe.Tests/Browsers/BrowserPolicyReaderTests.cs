using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Browsers;

public sealed class BrowserPolicyReaderTests : IDisposable
{
    private const string Chrome = @"SOFTWARE\Policies\Google\Chrome";
    private readonly BrowserFolders _folders = new();

    public void Dispose() => _folders.Dispose();

    private BrowserPolicyReader Reader(FakeRegistry registry) => new(registry, _folders.Locations);

    [Fact]
    public void No_policies_means_not_managed()
    {
        Assert.Empty(Reader(new FakeRegistry()).Read(Browser.Chrome));
    }

    [Fact]
    public void Lists_every_setting_for_the_whole_PC_and_for_this_account()
    {
        var registry = new FakeRegistry()
            .Set(RegistryRoot.LocalMachine, Chrome, "DefaultSearchProviderSearchURL", "https://hijack.example/?q={searchTerms}")
            .Set(RegistryRoot.LocalMachine, Chrome, "DefaultSearchProviderEnabled", 1)
            .Set(RegistryRoot.LocalMachine, $@"{Chrome}\ExtensionInstallForcelist", "1", "aaaabbbbccccddddeeeeffffgggghhhh;https://clients2.google.com/service/update2/crx")
            .Set(RegistryRoot.CurrentUser, Chrome, "HomepageLocation", "https://hijack.example");

        var found = Reader(registry).Read(Browser.Chrome);

        Assert.Equal(2, found.Count);
        Assert.Equal(RegistryRoot.LocalMachine, found[0].Root);
        Assert.Equal(
            ["DefaultSearchProviderEnabled = 1", "DefaultSearchProviderSearchURL = https://hijack.example/?q={searchTerms}", @"ExtensionInstallForcelist\1 = aaaabbbbccccddddeeeeffffgggghhhh;https://clients2.google.com/service/update2/crx"],
            found[0].Settings);
        Assert.Equal(["HomepageLocation = https://hijack.example"], found[1].Settings);
    }

    [Fact]
    public void Works_out_what_policies_force_for_search_and_start_pages()
    {
        var registry = new FakeRegistry()
            .Set(RegistryRoot.LocalMachine, Chrome, "DefaultSearchProviderName", "Hijack")
            .Set(RegistryRoot.LocalMachine, Chrome, "DefaultSearchProviderSearchURL", "https://hijack.example/?q={searchTerms}")
            .Set(RegistryRoot.CurrentUser, $@"{Chrome}\RestoreOnStartupURLs", "1", "https://start.example");

        var overrides = Reader(registry).Overrides(Browser.Chrome);

        Assert.Equal("Hijack", overrides.SearchName);
        Assert.Equal("https://hijack.example/?q={searchTerms}", overrides.SearchUrl);
        Assert.Equal(["https://start.example"], overrides.StartPages);
    }

    [Fact]
    public void Finds_add_ons_a_policy_forces_on()
    {
        var registry = new FakeRegistry()
            .Set(RegistryRoot.LocalMachine, $@"{Chrome}\ExtensionInstallForcelist", "1", "aaaabbbbccccddddeeeeffffgggghhhh;https://clients2.google.com/service/update2/crx")
            .Set(RegistryRoot.CurrentUser, $@"{Chrome}\ExtensionInstallForcelist", "1", "ppppoooolllliiiiccccyyyyaaaabbbb");

        Assert.Equal(new HashSet<string> { "aaaabbbbccccddddeeeeffffgggghhhh", "ppppoooolllliiiiccccyyyyaaaabbbb" }, Reader(registry).ForcedExtensionIds(Browser.Chrome));
    }

    [Fact]
    public void Firefox_policies_json_counts_as_a_policy_location()
    {
        Directory.CreateDirectory(Path.Combine(_folders.FirefoxInstall, "distribution"));
        File.WriteAllText(Path.Combine(_folders.FirefoxInstall, "distribution", "policies.json"), """
            { "policies": { "Homepage": { "URL": "https://hijack.example", "Locked": true }, "SearchEngines": { "Default": "Hijack" } } }
            """);

        var reader = Reader(new FakeRegistry());
        var location = Assert.Single(reader.Read(Browser.Firefox));
        var overrides = reader.Overrides(Browser.Firefox);

        Assert.Equal(PolicyPlace.File, location.Place);
        Assert.Contains(location.Settings, s => s.StartsWith("Homepage = ", StringComparison.Ordinal));
        Assert.Equal("Hijack", overrides.SearchName);
        Assert.Equal(["https://hijack.example"], overrides.StartPages);
    }

    [Fact]
    public void Firefox_registry_policies_are_read_too()
    {
        var registry = new FakeRegistry()
            .Set(RegistryRoot.LocalMachine, @"SOFTWARE\Policies\Mozilla\Firefox\Homepage", "URL", "https://forced.example")
            .Set(RegistryRoot.LocalMachine, @"SOFTWARE\Policies\Mozilla\Firefox\SearchEngines", "Default", "Forced");

        var overrides = Reader(registry).Overrides(Browser.Firefox);

        Assert.Equal("Forced", overrides.SearchName);
        Assert.Equal(["https://forced.example"], overrides.StartPages);
    }

    [Fact]
    public void Edge_and_Chrome_policies_are_kept_apart()
    {
        var registry = new FakeRegistry().Set(RegistryRoot.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "HomepageLocation", "https://edge.example");

        Assert.Single(Reader(registry).Read(Browser.Edge));
        Assert.Empty(Reader(registry).Read(Browser.Chrome));
    }
}
