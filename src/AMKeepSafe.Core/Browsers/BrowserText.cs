using System.Text.Json;
using AMKeepSafe.Core.Checks;

namespace AMKeepSafe.Core.Browsers;

/// <summary>How each browser is named, and the addresses of its own settings pages. From data/browser-check.json.</summary>
public sealed record BrowserInfo
{
    public required string Name { get; init; }

    public IReadOnlyDictionary<BrowserPage, string> Pages { get; init; } = new Dictionary<BrowserPage, string>();
}

public sealed record BrowserNotes
{
    /// <summary>Has {runningAs} and {signedIn} placeholders.</summary>
    public required string DifferentAccount { get; init; }

    public required string NoBrowsers { get; init; }

    public required string CloseTitle { get; init; }

    /// <summary>Has a {browser} placeholder.</summary>
    public required string CloseBody { get; init; }

    public required string CloseYes { get; init; }

    public required string CloseNo { get; init; }

    public required string StillOpen { get; init; }

    public required string CheckButton { get; init; }

    public required string CheckAgainButton { get; init; }

    public required string Intro { get; init; }

    public required string Checking { get; init; }
}

/// <summary>Everything the Browser check says: results worded like checks.json, plus browser names and pages.</summary>
public sealed class BrowserText
{
    public const string ResourceName = "AMKeepSafe.Core.Data.browser-check.json";

    public required CheckCatalog Catalog { get; init; }

    public required IReadOnlyDictionary<Browser, BrowserInfo> Browsers { get; init; }

    public required BrowserNotes Notes { get; init; }

    public static BrowserText LoadEmbedded() => Load(SearchEngineList.Embedded(ResourceName));

    public static BrowserText Load(Stream json)
    {
        using var memory = new MemoryStream();
        json.CopyTo(memory);

        memory.Position = 0;
        var catalog = CheckCatalog.Load(memory);

        memory.Position = 0;
        var extra = JsonSerializer.Deserialize<FileShape>(memory, CheckCatalog.JsonOptions) ?? throw new InvalidDataException("browser-check.json is empty.");

        return new BrowserText
        {
            Catalog = catalog,
            Browsers = extra.Browsers ?? throw new InvalidDataException("browser-check.json has no browsers section."),
            Notes = extra.Notes ?? throw new InvalidDataException("browser-check.json has no notes section."),
        };
    }

    public string NameOf(Browser browser) => Browsers.TryGetValue(browser, out var info) ? info.Name : browser.ToString();

    /// <summary>A browser page's address, only if it's one of that browser's own internal pages.</summary>
    public string? PageAddress(Browser browser, BrowserPage page)
    {
        if (!Browsers.TryGetValue(browser, out var info) || !info.Pages.TryGetValue(page, out var address))
        {
            return null;
        }

        var allowed = browser switch
        {
            Browser.Chrome => "chrome://",
            Browser.Edge => "edge://",
            _ => "about:",
        };
        return address.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ? address : null;
    }

    private sealed record FileShape
    {
        public Dictionary<Browser, BrowserInfo>? Browsers { get; init; }

        public BrowserNotes? Notes { get; init; }
    }
}
