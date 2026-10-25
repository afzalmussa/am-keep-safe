using AMKeepSafe.Core.Browsers;

namespace AMKeepSafe.Tests.Browsers;

public class SearchEngineListTests
{
    private static readonly SearchEngineList Engines = SearchEngineList.LoadEmbedded();

    [Theory]
    [InlineData("https://www.google.com/search?q={searchTerms}", "Google")]
    [InlineData("https://www.google.co.uk/search?q={searchTerms}", "Google")]
    [InlineData("https://google.de/search?q=x", "Google")]
    [InlineData("https://www.bing.com/search?q={searchTerms}&form=QBLH", "Bing")]
    [InlineData("https://duckduckgo.com/?q={searchTerms}", "DuckDuckGo")]
    [InlineData("https://uk.search.yahoo.com/search?p={searchTerms}", "Yahoo")]
    [InlineData("https://www.ecosia.org/search?q={searchTerms}", "Ecosia")]
    public void Recognises_well_known_engines_by_address(string url, string expected)
    {
        Assert.Equal(expected, Engines.Match(url, null)?.Name);
    }

    [Theory]
    [InlineData("https://google.search-results.example/?q={searchTerms}")]
    [InlineData("https://www.google.com.evil.example/search")]
    [InlineData("https://bing.com.example/search")]
    [InlineData("https://search.example.com/?q={searchTerms}")]
    [InlineData("http://192.0.2.1/search")]
    public void Lookalike_addresses_are_not_mistaken_for_the_real_thing(string url)
    {
        Assert.Null(Engines.Match(url, "Google"));
    }

    [Fact]
    public void Built_in_engines_stored_without_a_real_address_are_recognised_by_name()
    {
        Assert.Equal("Google", Engines.Match("{google:baseURL}search?q={searchTerms}", "Google")?.Name);
        Assert.Equal("Microsoft Bing", Engines.Match(null, "Microsoft Bing")?.Name);
        Assert.Null(Engines.Match(null, "Super Search Plus"));
    }

    [Theory]
    [InlineData("https://search.yahoo.com/yhs/search?hspart=example&p={searchTerms}", true)]
    [InlineData("https://search.yahoo.com/search?p={searchTerms}", false)]
    [InlineData(null, false)]
    public void Spots_searches_routed_through_a_partner_link(string? url, bool expected)
    {
        Assert.Equal(expected, Engines.IsPartnerLink(url));
    }
}
