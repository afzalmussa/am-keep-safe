using AMKeepSafe.Core.Checks;

namespace AMKeepSafe.Core.Browsers;

/// <summary>
/// Turns what was read from a browser into outcomes, worded by data/browser-check.json. Pure rules,
/// so each one is tested with plain facts. Unknown things are "Do you recognise this?", never harmful.
/// </summary>
public static class BrowserFindings
{
    public const string Search = "browser-search";
    public const string StartPages = "browser-start-pages";
    public const string Extensions = "browser-extensions";
    public const string Notifications = "browser-notifications";
    public const string Managed = "browser-managed";
    public const string Unreadable = "browser-unreadable";

    public static class Codes
    {
        public const string WellKnown = "wellKnown";
        public const string BrowserDefault = "browserDefault";
        public const string NotRecognised = "notRecognised";
        public const string SetByExtension = "setByExtension";
        public const string SetByPolicy = "setByPolicy";
        public const string PartnerLink = "partnerLink";
        public const string UserPages = "userPages";
        public const string None = "none";
        public const string AllFromStore = "allFromStore";
        public const string SomeToCheck = "someToCheck";
        public const string KnownBad = "knownBad";
        public const string Some = "some";
        public const string Present = "present";
        public const string PresentWorkPc = "presentWorkPc";
        public const string CouldNotRead = "couldNotRead";
    }

    /// <summary>The codes each finding can produce, so the tests can check every one has wording.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> OutcomeCodes { get; } = new Dictionary<string, IReadOnlyCollection<string>>
    {
        [Search] = [Codes.WellKnown, Codes.BrowserDefault, Codes.NotRecognised, Codes.SetByExtension, Codes.SetByPolicy, Codes.PartnerLink],
        [StartPages] = [Codes.BrowserDefault, Codes.UserPages, Codes.SetByExtension, Codes.SetByPolicy],
        [Extensions] = [Codes.None, Codes.AllFromStore, Codes.SomeToCheck, Codes.KnownBad],
        [Notifications] = [Codes.None, Codes.Some],
        [Managed] = [Codes.None, Codes.Present, Codes.PresentWorkPc],
        [Unreadable] = [Codes.CouldNotRead],
    };

    public sealed record ExtensionSettings
    {
        public string ItemStore { get; init; } = "{name}";

        public string ItemOutsideStore { get; init; } = "{name}: added outside the official store. Do you recognise it?";

        public string ItemPolicy { get; init; } = "{name}: put there by a setting on this PC";

        public string ItemKnownBad { get; init; } = "{name}: removed from the store after it was found to be harmful";

        public string SwitchedOff { get; init; } = " (switched off)";
    }

    public sealed record ManagedSettings
    {
        public int MaxItems { get; init; } = 12;

        public string AndMore { get; init; } = "and {count} more";
    }

    public static CheckOutcome EvaluateSearch(SearchSetting search, SearchEngineList engines)
    {
        var known = engines.Match(search.Url, search.Name);
        var engine = known?.Name ?? search.Name ?? SearchEngineList.TryHost(search.Url) ?? "an unnamed search engine";
        var values = new Dictionary<string, string> { ["engine"] = engine, ["by"] = search.SetByName ?? "" };
        var detail = $"Search: {search.Name ?? "?"} ({search.Url ?? "no address"}), set by {search.SetBy}{(search.SetByName is null ? "" : $" ({search.SetByName})")}. Recognised: {known?.Name ?? "no"}.";

        var code = search.SetBy switch
        {
            SetBy.Policy => Codes.SetByPolicy,
            SetBy.Extension => Codes.SetByExtension,
            _ when engines.IsPartnerLink(search.Url) => Codes.PartnerLink,
            SetBy.BrowserDefault when search.Url is null => Codes.BrowserDefault,
            _ when known is not null => Codes.WellKnown,
            _ => Codes.NotRecognised,
        };
        return new CheckOutcome(code, values, Detail: detail);
    }

    public static CheckOutcome EvaluateStartPages(StartPagesSetting pages)
    {
        var values = new Dictionary<string, string> { ["by"] = pages.SetByName ?? "" };
        var detail = $"Start pages: {(pages.Pages.Count == 0 ? "browser's own" : string.Join(", ", pages.Pages))}, set by {pages.SetBy}.";
        var code = pages.SetBy switch
        {
            SetBy.Policy => Codes.SetByPolicy,
            SetBy.Extension => Codes.SetByExtension,
            _ when pages.Pages.Count == 0 => Codes.BrowserDefault,
            _ => Codes.UserPages,
        };
        return new CheckOutcome(code, values, pages.Pages.Select(Shorten).ToList(), detail);
    }

    public static CheckOutcome EvaluateExtensions(IReadOnlyList<BrowserExtension> extensions, KnownBadExtensionList knownBad, ExtensionSettings text)
    {
        var detail = $"Add-ons: {(extensions.Count == 0 ? "none" : string.Join(", ", extensions.Select(e => $"{e.Name} [{e.Id}, {e.Source}{(e.Enabled ? "" : ", off")}{(knownBad.Contains(e.Id) ? ", KNOWN BAD" : "")}]")))}.";
        if (extensions.Count == 0)
        {
            return new CheckOutcome(Codes.None, Detail: detail);
        }

        int Rank(BrowserExtension e) => knownBad.Contains(e.Id) ? 0 : e.Source switch { ExtensionSource.Policy => 1, ExtensionSource.OutsideStore => 2, _ => 3 };
        var items = extensions
            .OrderBy(Rank)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e =>
            {
                var template = Rank(e) switch { 0 => text.ItemKnownBad, 1 => text.ItemPolicy, 2 => text.ItemOutsideStore, _ => text.ItemStore };
                return CheckCatalog.Fill(template, new Dictionary<string, string> { ["name"] = e.Name }) + (e.Enabled ? "" : text.SwitchedOff);
            })
            .ToList();

        var code = extensions.Any(e => knownBad.Contains(e.Id)) ? Codes.KnownBad
            : extensions.Any(e => e.Source != ExtensionSource.Store) ? Codes.SomeToCheck
            : Codes.AllFromStore;
        return new CheckOutcome(code, Items: items, Detail: detail);
    }

    public static CheckOutcome EvaluateNotifications(IReadOnlyList<string> sites) =>
        new(sites.Count == 0 ? Codes.None : Codes.Some, Items: sites.Select(Shorten).ToList(), Detail: $"Sites allowed to send notifications: {(sites.Count == 0 ? "none" : string.Join(", ", sites))}.");

    public static CheckOutcome EvaluateManaged(IReadOnlyList<PolicyLocation> policies, bool isWorkOrSchoolPc, ManagedSettings text)
    {
        var lines = policies.SelectMany(p => p.Settings).ToList();
        var detail = policies.Count == 0
            ? "No browser policies."
            : $"Policies: {string.Join(" | ", policies.Select(p => $"{p.Root?.ToString() ?? "file"} {p.Path}: {string.Join("; ", p.Settings)}"))}. Work or school PC: {isWorkOrSchoolPc}.";
        if (policies.Count == 0)
        {
            return new CheckOutcome(Codes.None, Detail: detail);
        }

        var items = lines.Take(text.MaxItems).Select(Shorten).ToList();
        if (lines.Count > text.MaxItems)
        {
            items.Add(CheckCatalog.Fill(text.AndMore, new Dictionary<string, string> { ["count"] = (lines.Count - text.MaxItems).ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        }

        return new CheckOutcome(isWorkOrSchoolPc ? Codes.PresentWorkPc : Codes.Present, Items: items, Detail: detail);
    }

    private static string Shorten(string text) => text.Length <= 120 ? text : text[..120] + "...";
}
