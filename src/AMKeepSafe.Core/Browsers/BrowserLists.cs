using System.Text.Json;
using System.Text.RegularExpressions;
using AMKeepSafe.Core.Checks;

namespace AMKeepSafe.Core.Browsers;

/// <summary>A well-known search engine, from data/search-engines.json.</summary>
public sealed record KnownSearchEngine
{
    public required string Name { get; init; }

    /// <summary>Patterns for the web address's host, such as "^(www\.)?bing\.com$".</summary>
    public IReadOnlyList<string> HostPatterns { get; init; } = [];
}

/// <summary>The well-known search engines, and signs a search has been routed through someone else's link.</summary>
public sealed class SearchEngineList
{
    public const string ResourceName = "AMKeepSafe.Core.Data.search-engines.json";

    private IReadOnlyList<(KnownSearchEngine Engine, Regex[] Hosts)> _compiled = [];

    public required IReadOnlyList<KnownSearchEngine> Engines { get; init; }

    /// <summary>Text in a search address that means it goes through another company's link first.</summary>
    public IReadOnlyList<string> PartnerLinkSigns { get; init; } = [];

    public static SearchEngineList LoadEmbedded() => Load(Embedded(ResourceName));

    public static SearchEngineList Load(Stream json)
    {
        var file = JsonSerializer.Deserialize<FileShape>(json, CheckCatalog.JsonOptions) ?? throw new InvalidDataException("search-engines.json is empty.");
        var list = new SearchEngineList { Engines = file.Engines ?? [], PartnerLinkSigns = file.PartnerLinkSigns ?? [] };
        list._compiled = list.Engines
            .Select(e => (e, e.HostPatterns.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray()))
            .ToList();
        return list;
    }

    /// <summary>The well-known engine a search address belongs to, or null.</summary>
    public KnownSearchEngine? Match(string? url, string? name)
    {
        if (TryHost(url) is { } host)
        {
            return _compiled.FirstOrDefault(c => c.Hosts.Any(h => h.IsMatch(host))).Engine;
        }

        // Some browsers store their built-in engines without a real address, for example "{google:baseURL}search".
        return name is null ? null : Engines.FirstOrDefault(e => string.Equals(e.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public bool IsPartnerLink(string? url) =>
        url is not null && PartnerLinkSigns.Any(s => url.Contains(s, StringComparison.OrdinalIgnoreCase));

    public static string? TryHost(string? url) =>
        url is not null && Uri.TryCreate(url.Replace("{searchTerms}", "x", StringComparison.Ordinal), UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.Host.Length > 0
            ? uri.Host
            : null;

    internal static Stream Embedded(string name) =>
        typeof(SearchEngineList).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Embedded file '{name}' is missing.");

    private sealed record FileShape
    {
        public List<KnownSearchEngine>? Engines { get; init; }

        public List<string>? PartnerLinkSigns { get; init; }
    }
}

/// <summary>An add-on known to be harmful, from data/extensions-known-bad.json.</summary>
public sealed record KnownBadExtension
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Source { get; init; }

    /// <summary>False until someone has checked the id against the source report.</summary>
    public bool Confirmed { get; init; }
}

public sealed class KnownBadExtensionList
{
    public const string ResourceName = "AMKeepSafe.Core.Data.extensions-known-bad.json";

    private HashSet<string> _ids = [];

    public required IReadOnlyList<KnownBadExtension> Extensions { get; init; }

    public static KnownBadExtensionList LoadEmbedded() => Load(SearchEngineList.Embedded(ResourceName));

    public static KnownBadExtensionList Load(Stream json)
    {
        var file = JsonSerializer.Deserialize<FileShape>(json, CheckCatalog.JsonOptions) ?? throw new InvalidDataException("extensions-known-bad.json is empty.");
        var list = new KnownBadExtensionList { Extensions = file.Extensions ?? [] };
        list._ids = new HashSet<string>(list.Extensions.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public bool Contains(string id) => _ids.Contains(id);

    private sealed record FileShape
    {
        public List<KnownBadExtension>? Extensions { get; init; }
    }
}
