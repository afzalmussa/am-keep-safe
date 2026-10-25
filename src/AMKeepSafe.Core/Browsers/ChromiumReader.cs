using System.Text.Json;
using System.Text.Json.Nodes;

namespace AMKeepSafe.Core.Browsers;

/// <summary>
/// Reads Edge and Chrome profiles: "Local State" for profile names, "Preferences" and
/// "Secure Preferences" for settings, and each add-on's manifest for its name. Only reads: Edge and
/// Chrome protect these files, and editing them could break the browser.
/// </summary>
public sealed class ChromiumReader(BrowserLocations locations)
{
    // Chromium's "location" codes for where an add-on came from.
    private const int Internal = 1;
    private const int Unpacked = 4;
    private const int Component = 5;
    private const int CommandLine = 8;
    private const int ExternalPolicyDownload = 7;
    private const int ExternalPolicy = 9;
    private const int ExternalComponent = 10;

    private static readonly string[] StoreUpdateHosts = ["clients2.google.com", "edge.microsoft.com"];

    public string UserDataFolder(Browser browser) => browser switch
    {
        Browser.Chrome => Path.Combine(locations.LocalAppData, "Google", "Chrome", "User Data"),
        Browser.Edge => Path.Combine(locations.LocalAppData, "Microsoft", "Edge", "User Data"),
        _ => throw new ArgumentOutOfRangeException(nameof(browser), browser, "Not a Chromium browser."),
    };

    public bool IsInstalledForThisAccount(Browser browser) => Directory.Exists(UserDataFolder(browser));

    public IReadOnlyList<BrowserProfile> ReadProfiles(Browser browser, PolicyOverrides policy, IReadOnlySet<string> forcedExtensionIds)
    {
        var userData = UserDataFolder(browser);
        if (!Directory.Exists(userData))
        {
            return [];
        }

        return ProfileFolders(userData)
            .Select(p => ReadProfile(browser, userData, p.Key, p.Name, policy, forcedExtensionIds))
            .ToList();
    }

    /// <summary>Profile folders with their friendly names, from Local State, falling back to the folders themselves.</summary>
    private static IReadOnlyList<(string Key, string Name)> ProfileFolders(string userData)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Json(Path.Combine(userData, "Local State"))?["profile"]?["info_cache"]?.AsObject() is { } cache)
            {
                foreach (var (key, info) in cache)
                {
                    names[key] = info?["name"]?.GetValue<string>() ?? key;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Fall back to folder names.
        }

        return Directory.EnumerateDirectories(userData)
            .Select(Path.GetFileName)
            .Where(name => name is not null && File.Exists(Path.Combine(userData, name, "Preferences")))
            .Select(name => (name!, names.TryGetValue(name!, out var friendly) ? friendly : name!))
            .OrderBy(p => p.Item1 == "Default" ? 0 : 1)
            .ThenBy(p => p.Item1, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static BrowserProfile ReadProfile(Browser browser, string userData, string key, string name, PolicyOverrides policy, IReadOnlySet<string> forced)
    {
        var folder = Path.Combine(userData, key);
        try
        {
            var prefs = Json(Path.Combine(folder, "Preferences")) ?? new JsonObject();
            var secure = Json(Path.Combine(folder, "Secure Preferences")) ?? new JsonObject();

            var extensions = ReadExtensions(folder, prefs, secure, forced);
            var overrides = extensions.Where(e => e.Info.Enabled).Select(e => (e.Info, e.Manifest?["chrome_settings_overrides"])).Where(x => x.Item2 is not null).ToList();

            return new BrowserProfile(
                browser,
                key,
                name,
                ReadSearch(browser, prefs, policy, overrides),
                ReadStartPages(prefs, policy, overrides),
                extensions.Select(e => e.Info).ToList(),
                ReadNotificationSites(prefs));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new BrowserProfile(browser, key, name, new SearchSetting(null, null, SetBy.BrowserDefault), new StartPagesSetting([], SetBy.BrowserDefault), [], [], ex.Message);
        }
    }

    private static List<(BrowserExtension Info, JsonNode? Manifest)> ReadExtensions(string folder, JsonNode prefs, JsonNode secure, IReadOnlySet<string> forced)
    {
        // Newer versions keep add-ons in Secure Preferences, older ones in Preferences. Secure wins.
        var settings = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in new[] { prefs, secure })
        {
            if (source["extensions"]?["settings"]?.AsObject() is { } list)
            {
                foreach (var (id, value) in list)
                {
                    settings[id] = value;
                }
            }
        }

        var found = new List<(BrowserExtension, JsonNode?)>();
        foreach (var (id, setting) in settings)
        {
            var location = Int(setting?["location"]);
            if (location is Component or ExternalComponent)
            {
                continue;
            }

            var manifest = setting?["manifest"] ?? ManifestFromDisk(folder, id, setting?["path"]?.GetValue<string>());
            if (manifest is null && location is null)
            {
                // Leftover settings for an add-on that's no longer installed.
                continue;
            }

            if (manifest?["theme"] is not null)
            {
                continue;
            }

            var displayName = LocalisedName(manifest, folder, id, setting?["path"]?.GetValue<string>()) ?? id;
            var source = forced.Contains(id) || location is ExternalPolicy or ExternalPolicyDownload
                ? ExtensionSource.Policy
                : location is Unpacked or CommandLine ? ExtensionSource.OutsideStore
                : IsFromStore(setting, manifest) ? ExtensionSource.Store
                : ExtensionSource.OutsideStore;

            found.Add((new BrowserExtension(id, displayName, source, IsEnabled(setting)), manifest));
        }

        return found.OrderBy(e => e.Item1.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsFromStore(JsonNode? setting, JsonNode? manifest)
    {
        if (setting?["from_webstore"]?.GetValue<bool>() == true)
        {
            return true;
        }

        var update = manifest?["update_url"]?.GetValue<string>();
        return update is not null && Uri.TryCreate(update, UriKind.Absolute, out var uri)
            && StoreUpdateHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
            && Int(setting?["location"]) is null or Internal;
    }

    private static bool IsEnabled(JsonNode? setting)
    {
        if (Int(setting?["state"]) is { } state)
        {
            return state == 1;
        }

        return setting?["disable_reasons"] switch
        {
            null => true,
            JsonArray reasons => reasons.Count == 0,
            JsonValue reasons when reasons.TryGetValue<int>(out var r) => r == 0,
            _ => true,
        };
    }

    private static JsonNode? ManifestFromDisk(string profileFolder, string id, string? relativePath) =>
        ExtensionFolder(profileFolder, id, relativePath) is { } folder ? Json(Path.Combine(folder, "manifest.json")) : null;

    /// <summary>The add-on's own folder: from its recorded path, or the newest version under Extensions\id.</summary>
    private static string? ExtensionFolder(string profileFolder, string id, string? relativePath)
    {
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            var candidate = Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(profileFolder, "Extensions", relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        var versions = Path.Combine(profileFolder, "Extensions", id);
        return Directory.Exists(versions)
            ? Directory.EnumerateDirectories(versions).OrderByDescending(v => v, StringComparer.Ordinal).FirstOrDefault()
            : null;
    }

    /// <summary>Resolves names like "__MSG_appName__" from the add-on's language files.</summary>
    private static string? LocalisedName(JsonNode? manifest, string profileFolder, string id, string? relativePath)
    {
        var name = manifest?["name"]?.GetValue<string>();
        if (name is null || !name.StartsWith("__MSG_", StringComparison.Ordinal) || !name.EndsWith("__", StringComparison.Ordinal))
        {
            return Tidy(name);
        }

        var message = name[6..^2];
        var folder = ExtensionFolder(profileFolder, id, relativePath);
        if (folder is null)
        {
            return null;
        }

        var defaultLocale = manifest?["default_locale"]?.GetValue<string>();
        foreach (var locale in new[] { "en_GB", "en", defaultLocale }.Where(l => l is not null).Distinct())
        {
            var messages = Json(Path.Combine(folder, "_locales", locale!, "messages.json"))?.AsObject();
            var match = messages?.FirstOrDefault(m => string.Equals(m.Key, message, StringComparison.OrdinalIgnoreCase)).Value;
            if (match?["message"]?.GetValue<string>() is { } text)
            {
                return Tidy(text);
            }
        }

        return null;
    }

    private static SearchSetting ReadSearch(Browser browser, JsonNode prefs, PolicyOverrides policy, List<(BrowserExtension Info, JsonNode? Overrides)> overrides)
    {
        if (policy.SearchUrl is not null || policy.SearchName is not null)
        {
            return new SearchSetting(policy.SearchName, policy.SearchUrl, SetBy.Policy);
        }

        foreach (var (info, settings) in overrides)
        {
            if (settings?["search_provider"] is { } provider && provider["is_default"]?.GetValue<bool>() != false)
            {
                return new SearchSetting(provider["name"]?.GetValue<string>(), provider["search_url"]?.GetValue<string>(), SetBy.Extension, info.Name);
            }
        }

        if (prefs["default_search_provider_data"]?["template_url_data"] is { } chosen)
        {
            return new SearchSetting(chosen["short_name"]?.GetValue<string>(), chosen["url"]?.GetValue<string>(), SetBy.User);
        }

        return new SearchSetting(browser == Browser.Edge ? "Bing" : "Google", null, SetBy.BrowserDefault);
    }

    private static StartPagesSetting ReadStartPages(JsonNode prefs, PolicyOverrides policy, List<(BrowserExtension Info, JsonNode? Overrides)> overrides)
    {
        if (policy.StartPages.Count > 0)
        {
            return new StartPagesSetting(policy.StartPages, SetBy.Policy);
        }

        foreach (var (info, settings) in overrides)
        {
            var pages = Strings(settings?["startup_pages"]);
            if (settings?["homepage"]?.GetValue<string>() is { } home)
            {
                pages.Add(home);
            }

            if (pages.Count > 0)
            {
                return new StartPagesSetting(pages.Distinct().ToList(), SetBy.Extension, info.Name);
            }
        }

        var chosen = new List<string>();

        // 4 means "open a specific page or set of pages".
        if (Int(prefs["session"]?["restore_on_startup"]) == 4)
        {
            chosen.AddRange(Strings(prefs["session"]?["startup_urls"]));
        }

        if (prefs["homepage_is_newtabpage"]?.GetValue<bool>() == false && prefs["homepage"]?.GetValue<string>() is { Length: > 0 } homepage)
        {
            chosen.Add(homepage);
        }

        return chosen.Count == 0 ? new StartPagesSetting([], SetBy.BrowserDefault) : new StartPagesSetting(chosen.Distinct().ToList(), SetBy.User);
    }

    private static IReadOnlyList<string> ReadNotificationSites(JsonNode prefs)
    {
        if (prefs["profile"]?["content_settings"]?["exceptions"]?["notifications"]?.AsObject() is not { } sites)
        {
            return [];
        }

        // Keys look like "https://example.com:443,*". A setting of 1 means allowed.
        return sites
            .Where(s => Int(s.Value?["setting"]) == 1)
            .Select(s => s.Key.Split(',')[0].Replace(":443", "", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static JsonNode? Json(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        // The browser may have the file open, so share it rather than locking it.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
    }

    private static int? Int(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    private static List<string> Strings(JsonNode? node) =>
        node is JsonArray array ? array.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList() : [];

    /// <summary>Add-on names come from their makers, so keep them to a sensible length on one line.</summary>
    internal static string? Tidy(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var oneLine = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= 80 ? oneLine : oneLine[..80] + "...";
    }
}
