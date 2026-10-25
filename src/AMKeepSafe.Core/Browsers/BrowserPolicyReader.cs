using System.Text.Json.Nodes;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Core.Browsers;

/// <summary>What policies force for search and start pages, which the readers report as "set by a policy".</summary>
public sealed record PolicyOverrides(string? SearchName, string? SearchUrl, IReadOnlyList<string> StartPages)
{
    public static PolicyOverrides None { get; } = new(null, null, []);
}

/// <summary>
/// Finds browser policies: the settings behind "Managed by your organisation". On a work PC these
/// are normal. On a home PC they're often put there by unwanted software to lock in a search engine,
/// home page or add-on.
/// </summary>
public sealed class BrowserPolicyReader(IRegistryReader registry, BrowserLocations locations)
{
    private const int MaxLines = 200;

    public static string RegistryKey(Browser browser) => browser switch
    {
        Browser.Edge => @"SOFTWARE\Policies\Microsoft\Edge",
        Browser.Chrome => @"SOFTWARE\Policies\Google\Chrome",
        _ => @"SOFTWARE\Policies\Mozilla\Firefox",
    };

    /// <summary>Every place policies are set for this browser. Empty means it isn't managed.</summary>
    public IReadOnlyList<PolicyLocation> Read(Browser browser)
    {
        var found = new List<PolicyLocation>();
        foreach (var root in new[] { RegistryRoot.LocalMachine, RegistryRoot.CurrentUser })
        {
            var lines = new List<string>();
            Collect(root, RegistryKey(browser), "", lines, depth: 0);
            if (lines.Count > 0)
            {
                found.Add(new PolicyLocation(PolicyPlace.Registry, root, RegistryKey(browser), lines));
            }
        }

        if (browser == Browser.Firefox)
        {
            foreach (var file in FirefoxPolicyFiles())
            {
                found.Add(new PolicyLocation(PolicyPlace.File, null, file, ReadPolicyFile(file)));
            }
        }

        return found;
    }

    /// <summary>Firefox's policies.json files that exist.</summary>
    public IReadOnlyList<string> FirefoxPolicyFiles() =>
        locations.FirefoxInstallFolders
            .Select(folder => Path.Combine(folder, "distribution", "policies.json"))
            .Where(File.Exists)
            .ToList();

    /// <summary>What the policies force for search and start pages. The local machine's settings win over the account's.</summary>
    public PolicyOverrides Overrides(Browser browser)
    {
        var key = RegistryKey(browser);
        string? Read(string name, string? subKey = null) =>
            new[] { RegistryRoot.LocalMachine, RegistryRoot.CurrentUser }
                .Select(root => registry.GetValue(root, subKey is null ? key : $@"{key}\{subKey}", name)?.ToString())
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        if (browser == Browser.Firefox)
        {
            var fromFile = FirefoxPolicyFiles().Select(ReadFirefoxFileOverrides).FirstOrDefault(o => o != PolicyOverrides.None);
            var homepage = Read("URL", "Homepage");
            return new PolicyOverrides(
                Read("Default", "SearchEngines") ?? fromFile?.SearchName,
                null,
                homepage is null ? fromFile?.StartPages ?? [] : [homepage]);
        }

        var startUrls = new[] { RegistryRoot.LocalMachine, RegistryRoot.CurrentUser }
            .Select(root => registry.GetValueNames(root, $@"{key}\RestoreOnStartupURLs")
                .Select(n => registry.GetValue(root, $@"{key}\RestoreOnStartupURLs", n)?.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToList())
            .FirstOrDefault(list => list.Count > 0) ?? [];

        var pages = startUrls.Count > 0 ? startUrls : Read("HomepageLocation") is { } home ? [home] : new List<string>();
        return new PolicyOverrides(Read("DefaultSearchProviderName"), Read("DefaultSearchProviderSearchURL"), pages);
    }

    /// <summary>Add-ons a policy forces on (ExtensionInstallForcelist entries look like "id;update address").</summary>
    public IReadOnlySet<string> ForcedExtensionIds(Browser browser)
    {
        var list = $@"{RegistryKey(browser)}\ExtensionInstallForcelist";
        return new[] { RegistryRoot.LocalMachine, RegistryRoot.CurrentUser }
            .SelectMany(root => registry.GetValueNames(root, list).Select(n => registry.GetValue(root, list, n)?.ToString()))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Split(';')[0].Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void Collect(RegistryRoot root, string key, string prefix, List<string> lines, int depth)
    {
        if (depth > 4 || lines.Count >= MaxLines)
        {
            return;
        }

        foreach (var name in registry.GetValueNames(root, key).Order(StringComparer.OrdinalIgnoreCase))
        {
            var value = registry.GetValue(root, key, name) switch
            {
                string[] many => string.Join("; ", many),
                byte[] bytes => $"({bytes.Length} bytes)",
                var other => other?.ToString() ?? "",
            };
            lines.Add($"{prefix}{(name.Length == 0 ? "(default)" : name)} = {value}");
        }

        foreach (var sub in registry.GetSubKeyNames(root, key).Order(StringComparer.OrdinalIgnoreCase))
        {
            Collect(root, $@"{key}\{sub}", $@"{prefix}{sub}\", lines, depth + 1);
        }
    }

    private static IReadOnlyList<string> ReadPolicyFile(string path)
    {
        try
        {
            var policies = JsonNode.Parse(File.ReadAllText(path))?["policies"]?.AsObject();
            return policies is null ? ["(no policies in the file)"] : policies.Select(p => $"{p.Key} = {Shorten(p.Value?.ToJsonString())}").ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return ["(the file couldn't be read)"];
        }
    }

    private static PolicyOverrides ReadFirefoxFileOverrides(string path)
    {
        try
        {
            var policies = JsonNode.Parse(File.ReadAllText(path))?["policies"];
            var homepage = policies?["Homepage"]?["URL"]?.GetValue<string>();
            var search = policies?["SearchEngines"]?["Default"]?.GetValue<string>();
            return search is null && homepage is null ? PolicyOverrides.None : new PolicyOverrides(search, null, homepage is null ? [] : [homepage]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return PolicyOverrides.None;
        }
    }

    private static string Shorten(string? text) => text is null ? "" : text.Length <= 200 ? text : text[..200] + "...";
}
