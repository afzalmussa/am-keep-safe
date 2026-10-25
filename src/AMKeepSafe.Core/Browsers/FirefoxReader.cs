using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace AMKeepSafe.Core.Browsers;

/// <summary>
/// Reads Firefox profiles: profiles.ini, extensions.json, extension-settings.json, prefs.js,
/// search.json.mozlz4 and permissions.sqlite. Only reads, and the database is copied first so
/// Firefox's own copy is never touched.
/// </summary>
public sealed partial class FirefoxReader(BrowserLocations locations)
{
    // Firefox's own built-in add-ons, which aren't worth listing.
    private static readonly string[] BuiltInLocations = ["app-builtin", "app-system-defaults", "app-system-addons"];

    private static readonly string[] StoreSources = ["amo", "disco", "about:addons"];

    public string FirefoxFolder => Path.Combine(locations.RoamingAppData, "Mozilla", "Firefox");

    public bool IsInstalledForThisAccount => File.Exists(Path.Combine(FirefoxFolder, "profiles.ini"));

    public IReadOnlyList<BrowserProfile> ReadProfiles(PolicyOverrides policy) =>
        ProfileFolders().Select(p => ReadProfile(p.Key, p.Name, p.Folder, policy)).ToList();

    /// <summary>Profiles listed in profiles.ini whose folders exist.</summary>
    private IReadOnlyList<(string Key, string Name, string Folder)> ProfileFolders()
    {
        var ini = Path.Combine(FirefoxFolder, "profiles.ini");
        if (!File.Exists(ini))
        {
            return [];
        }

        var profiles = new List<(string, string, string)>();
        string? section = null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Finish()
        {
            if (section?.StartsWith("Profile", StringComparison.OrdinalIgnoreCase) == true && values.TryGetValue("Path", out var path))
            {
                var relative = !values.TryGetValue("IsRelative", out var isRelative) || isRelative == "1";
                var folder = relative ? Path.Combine(FirefoxFolder, path.Replace('/', Path.DirectorySeparatorChar)) : path;
                if (Directory.Exists(folder))
                {
                    profiles.Add((Path.GetFileName(folder), values.GetValueOrDefault("Name") ?? Path.GetFileName(folder), folder));
                }
            }

            values.Clear();
        }

        foreach (var raw in File.ReadAllLines(ini))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Finish();
                section = line[1..^1];
            }
            else if (line.IndexOf('=') is var eq and > 0)
            {
                values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
        }

        Finish();
        return profiles;
    }

    private static BrowserProfile ReadProfile(string key, string name, string folder, PolicyOverrides policy)
    {
        try
        {
            var extensions = ReadExtensions(folder);
            var controlled = ReadExtensionControlledSettings(folder);
            string? NameOf(string? id) => id is null ? null : extensions.FirstOrDefault(e => e.Id == id)?.Name ?? id;

            return new BrowserProfile(
                Browser.Firefox,
                key,
                name,
                ReadSearch(folder, policy, controlled.SearchExtensionId is { } searchBy ? NameOf(searchBy) : null),
                ReadStartPages(folder, policy, controlled.HomepageExtensionId, controlled.Homepage, NameOf(controlled.HomepageExtensionId)),
                extensions,
                ReadNotificationSites(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or InvalidDataException or SqliteException)
        {
            return new BrowserProfile(Browser.Firefox, key, name, new SearchSetting(null, null, SetBy.BrowserDefault), new StartPagesSetting([], SetBy.BrowserDefault), [], [], ex.Message);
        }
    }

    private static List<BrowserExtension> ReadExtensions(string folder)
    {
        if (Json(Path.Combine(folder, "extensions.json"))?["addons"] is not JsonArray addons)
        {
            return [];
        }

        var found = new List<BrowserExtension>();
        foreach (var addon in addons)
        {
            var location = Str(addon?["location"]) ?? "";
            if (Str(addon?["type"]) != "extension" || BuiltInLocations.Any(b => location.StartsWith(b, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var id = Str(addon?["id"]) ?? "";
            var installSource = Str(addon?["installTelemetryInfo"]?["source"]);
            var sourceUri = Str(addon?["sourceURI"]);
            var fromStore = StoreSources.Contains(installSource, StringComparer.OrdinalIgnoreCase)
                || (sourceUri is not null && Uri.TryCreate(sourceUri, UriKind.Absolute, out var uri) && uri.Host.EndsWith("addons.mozilla.org", StringComparison.OrdinalIgnoreCase));

            var source = installSource == "enterprise-policy" ? ExtensionSource.Policy
                : fromStore && location == "app-profile" ? ExtensionSource.Store
                : ExtensionSource.OutsideStore;

            var active = addon?["active"] is JsonValue a && a.TryGetValue<bool>(out var isActive) && isActive;
            found.Add(new BrowserExtension(id, ChromiumReader.Tidy(Str(addon?["defaultLocale"]?["name"])) ?? id, source, active));
        }

        return found.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Which add-ons have taken over the search engine or home page, from extension-settings.json.</summary>
    private static (string? SearchExtensionId, string? HomepageExtensionId, string? Homepage) ReadExtensionControlledSettings(string folder)
    {
        var settings = Json(Path.Combine(folder, "extension-settings.json"));
        static JsonNode? Winner(JsonNode? setting) =>
            (setting?["precedenceList"] as JsonArray)?.FirstOrDefault(p => p?["enabled"] is not JsonValue e || !e.TryGetValue<bool>(out var on) || on);

        var search = Winner(settings?["default_search"]?["defaultSearch"] ?? settings?["default_search"]);
        var home = Winner(settings?["prefs"]?["homepage_override"]);
        return (Str(search?["id"]), Str(home?["id"]), Str(home?["value"]));
    }

    private static SearchSetting ReadSearch(string folder, PolicyOverrides policy, string? extensionName)
    {
        if (policy.SearchName is not null)
        {
            return new SearchSetting(policy.SearchName, null, SetBy.Policy);
        }

        var file = Path.Combine(folder, "search.json.mozlz4");
        if (!File.Exists(file))
        {
            return new SearchSetting(null, null, SetBy.BrowserDefault);
        }

        var json = JsonNode.Parse(Encoding.UTF8.GetString(Lz4Block.DecodeMozLz4(ReadShared(file))));
        var meta = json?["metaData"];
        var engines = json?["engines"] as JsonArray ?? [];
        var defaultId = Str(meta?["defaultEngineId"]);
        var current = Str(meta?["current"]);

        var engine = engines.FirstOrDefault(e => defaultId is not null && Str(e?["id"]) == defaultId)
                     ?? engines.FirstOrDefault(e => current is not null && Str(e?["_name"]) == current);
        if (engine is null)
        {
            return new SearchSetting(null, null, SetBy.BrowserDefault);
        }

        var engineName = Str(engine["_name"]);
        var url = Str((engine["_urls"] as JsonArray)?.FirstOrDefault()?["template"]);
        // Firefox's own engines have ids ending "@search.mozilla.org". Anything else with an add-on id came from an add-on.
        var byExtension = extensionName is not null && Str(engine["_extensionID"]) is { } extId && !extId.EndsWith("@search.mozilla.org", StringComparison.OrdinalIgnoreCase);
        return new SearchSetting(engineName, url, byExtension ? SetBy.Extension : SetBy.User, byExtension ? extensionName : null);
    }

    private static StartPagesSetting ReadStartPages(string folder, PolicyOverrides policy, string? extensionId, string? extensionHomepage, string? extensionName)
    {
        if (policy.StartPages.Count > 0)
        {
            return new StartPagesSetting(policy.StartPages, SetBy.Policy);
        }

        if (extensionId is not null && !string.IsNullOrWhiteSpace(extensionHomepage))
        {
            return new StartPagesSetting(SplitPages(extensionHomepage), SetBy.Extension, extensionName);
        }

        var prefs = ReadPrefs(Path.Combine(folder, "prefs.js"));

        // browser.startup.page: 1 shows the home page, 3 restores the last session.
        if (prefs.TryGetValue("browser.startup.page", out var page) && page == "3")
        {
            return new StartPagesSetting([], SetBy.BrowserDefault);
        }

        var pages = prefs.TryGetValue("browser.startup.homepage", out var homepage) ? SplitPages(homepage) : [];
        return pages.Count == 0 ? new StartPagesSetting([], SetBy.BrowserDefault) : new StartPagesSetting(pages, SetBy.User);
    }

    private static List<string> SplitPages(string value) =>
        value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !p.StartsWith("about:", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Reads user_pref("name", value) lines from prefs.js.</summary>
    internal static Dictionary<string, string> ReadPrefs(string path)
    {
        var prefs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return prefs;
        }

        foreach (Match match in UserPref().Matches(Encoding.UTF8.GetString(ReadShared(path))))
        {
            var value = match.Groups["value"].Value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
            }

            prefs[match.Groups["name"].Value] = value;
        }

        return prefs;
    }

    private static IReadOnlyList<string> ReadNotificationSites(string folder)
    {
        var database = Path.Combine(folder, "permissions.sqlite");
        if (!File.Exists(database))
        {
            return [];
        }

        // Copy the database, and its journal if there is one, so Firefox's own files aren't touched or locked.
        var temp = Path.Combine(Path.GetTempPath(), $"amkeepsafe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            var copy = Path.Combine(temp, "permissions.sqlite");
            File.WriteAllBytes(copy, ReadShared(database));
            if (File.Exists(database + "-wal"))
            {
                File.WriteAllBytes(copy + "-wal", ReadShared(database + "-wal"));
            }

            var sites = new List<string>();
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT DISTINCT origin FROM moz_perms WHERE type = 'desktop-notification' AND permission = 1 ORDER BY origin";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    sites.Add(reader.GetString(0));
                }
            }

            return sites;
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp copy is harmless.
            }
        }
    }

    private static JsonNode? Json(string path) =>
        File.Exists(path) ? JsonNode.Parse(ReadShared(path), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) : null;

    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    [GeneratedRegex("""user_pref\(\s*"(?<name>[^"]+)"\s*,\s*(?<value>"(?:[^"\\]|\\.)*"|[^)]*)\s*\)\s*;""")]
    private static partial Regex UserPref();
}
