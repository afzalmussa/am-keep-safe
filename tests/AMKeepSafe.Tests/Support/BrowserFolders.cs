using System.Text;
using System.Text.Json.Nodes;
using AMKeepSafe.Core.Browsers;
using Microsoft.Data.Sqlite;

namespace AMKeepSafe.Tests.Support;

/// <summary>Builds realistic browser profile folders in a temp directory, the way Edge, Chrome and Firefox lay them out.</summary>
public sealed class BrowserFolders : IDisposable
{
    private readonly TempDirectory _temp = new();

    public string Local => _temp.Combine("Local");

    public string Roaming => _temp.Combine("Roaming");

    public string FirefoxInstall => _temp.Combine("Program Files", "Mozilla Firefox");

    public BrowserLocations Locations => new(Local, Roaming, [FirefoxInstall]);

    public void Dispose() => _temp.Dispose();

    public string ChromiumUserData(Browser browser) => browser == Browser.Edge
        ? Path.Combine(Local, "Microsoft", "Edge", "User Data")
        : Path.Combine(Local, "Google", "Chrome", "User Data");

    /// <summary>Writes a Chromium profile. Preferences and Secure Preferences are JSON objects.</summary>
    public string ChromiumProfile(Browser browser, string key, JsonObject preferences, JsonObject? securePreferences = null, string? friendlyName = null)
    {
        var userData = ChromiumUserData(browser);
        var folder = Path.Combine(userData, key);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Preferences"), preferences.ToJsonString());
        if (securePreferences is not null)
        {
            File.WriteAllText(Path.Combine(folder, "Secure Preferences"), securePreferences.ToJsonString());
        }

        var localState = Path.Combine(userData, "Local State");
        var state = File.Exists(localState) ? JsonNode.Parse(File.ReadAllText(localState))!.AsObject() : new JsonObject { ["profile"] = new JsonObject { ["info_cache"] = new JsonObject() } };
        state["profile"]!["info_cache"]![key] = new JsonObject { ["name"] = friendlyName ?? key };
        File.WriteAllText(localState, state.ToJsonString());
        return folder;
    }

    /// <summary>Writes an add-on's manifest (and optional language file) into a Chromium profile's Extensions folder.</summary>
    public void ChromiumExtensionFiles(string profileFolder, string id, JsonObject manifest, string? englishMessages = null)
    {
        var folder = Path.Combine(profileFolder, "Extensions", id, "1.0.0_0");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.json"), manifest.ToJsonString());
        if (englishMessages is not null)
        {
            Directory.CreateDirectory(Path.Combine(folder, "_locales", "en"));
            File.WriteAllText(Path.Combine(folder, "_locales", "en", "messages.json"), englishMessages);
        }
    }

    /// <summary>Writes a Firefox profile listed in profiles.ini.</summary>
    public string FirefoxProfile(string folderName, string name)
    {
        var firefox = Path.Combine(Roaming, "Mozilla", "Firefox");
        var folder = Path.Combine(firefox, "Profiles", folderName);
        Directory.CreateDirectory(folder);
        var ini = Path.Combine(firefox, "profiles.ini");
        var index = File.Exists(ini) ? File.ReadAllText(ini).Split("[Profile").Length - 1 : 0;
        File.AppendAllText(ini, $"[Profile{index}]\nName={name}\nIsRelative=1\nPath=Profiles/{folderName}\n\n");
        return folder;
    }

    public static void FirefoxSearch(string profileFolder, string json) =>
        File.WriteAllBytes(Path.Combine(profileFolder, "search.json.mozlz4"), MozLz4(json));

    public static void FirefoxNotifications(string profileFolder, params (string Origin, int Permission)[] rows)
    {
        var path = Path.Combine(profileFolder, "permissions.sqlite");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE moz_perms (id INTEGER PRIMARY KEY, origin TEXT, type TEXT, permission INTEGER, expireType INTEGER, expireTime INTEGER, modificationTime INTEGER)";
        create.ExecuteNonQuery();
        foreach (var (origin, permission) in rows)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO moz_perms (origin, type, permission, expireType, expireTime, modificationTime) VALUES ($o, 'desktop-notification', $p, 0, 0, 0)";
            insert.Parameters.AddWithValue("$o", origin);
            insert.Parameters.AddWithValue("$p", permission);
            insert.ExecuteNonQuery();
        }

        using var other = connection.CreateCommand();
        other.CommandText = "INSERT INTO moz_perms (origin, type, permission, expireType, expireTime, modificationTime) VALUES ('https://maps.example', 'geo', 1, 0, 0, 0)";
        other.ExecuteNonQuery();
    }

    /// <summary>The mozLz4 format Firefox uses, with the data stored as one run of literals (valid LZ4).</summary>
    public static byte[] MozLz4(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        var file = new List<byte>("mozLz40\0"u8.ToArray());
        file.AddRange(BitConverter.GetBytes(data.Length));
        file.AddRange(Lz4LiteralsOnly(data));
        return file.ToArray();
    }

    public static byte[] Lz4LiteralsOnly(byte[] data)
    {
        var block = new List<byte>();
        if (data.Length < 15)
        {
            block.Add((byte)(data.Length << 4));
        }
        else
        {
            block.Add(0xF0);
            var rest = data.Length - 15;
            while (rest >= 255)
            {
                block.Add(255);
                rest -= 255;
            }

            block.Add((byte)rest);
        }

        block.AddRange(data);
        return block.ToArray();
    }
}
