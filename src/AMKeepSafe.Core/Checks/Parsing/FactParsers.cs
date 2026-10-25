using System.Globalization;
using System.Text.Json;
using AMKeepSafe.Core.Checks.Rules;

namespace AMKeepSafe.Core.Checks.Parsing;

/// <summary>
/// Turns the JSON printed by the Windows project's PowerShell scripts into facts. Kept here, away
/// from Windows, so it can be tested with sample output on any computer. Anything missing or
/// unexpected becomes "unknown" rather than a guess.
/// </summary>
public static class FactParsers
{
    public static WindowsUpdateStatus WindowsUpdateStatus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var start = String(root, "serviceStart") switch
        {
            "Automatic" or "AutomaticDelayedStart" => ServiceStart.Automatic,
            "Manual" => ServiceStart.Manual,
            "Disabled" => ServiceStart.Disabled,
            _ => ServiceStart.Unknown,
        };
        return new WindowsUpdateStatus(start, Date(root, "lastInstalled"), Int(root, "noAutoUpdate") == 1, Date(root, "pausedUntil"));
    }

    public static int? WaitingUpdates(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Int(doc.RootElement, "count");
    }

    public static AntivirusFacts Antivirus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        IReadOnlyList<AntivirusProduct>? products = null;
        if (root.TryGetProperty("products", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            products = list.EnumerateArray()
                .Where(p => String(p, "name") is { Length: > 0 } && Int(p, "state") is not null)
                .Select(p =>
                {
                    var state = Int(p, "state")!.Value;
                    return new AntivirusProduct(String(p, "name")!, SecurityCenterState.IsOn(state), SecurityCenterState.IsUpToDate(state));
                })
                .ToList();
        }

        DefenderStatus? defender = null;
        if (root.TryGetProperty("defender", out var d) && d.ValueKind == JsonValueKind.Object)
        {
            defender = new DefenderStatus(
                Bool(d, "antivirusEnabled") ?? false,
                Bool(d, "realTimeProtectionEnabled") ?? false,
                Date(d, "signaturesUpdated"),
                String(d, "amRunningMode"));
        }

        return new AntivirusFacts(products, defender);
    }

    public static FirewallFacts Firewall(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var profiles = new Dictionary<NetworkProfile, bool>();
        if (root.TryGetProperty("profiles", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in list.EnumerateArray())
            {
                if (Enum.TryParse<NetworkProfile>(String(p, "name"), ignoreCase: true, out var profile) && Bool(p, "enabled") is { } on)
                {
                    profiles[profile] = on;
                }
            }
        }

        var others = new List<string>();
        if (root.TryGetProperty("others", out var products) && products.ValueKind == JsonValueKind.Array)
        {
            others.AddRange(products.EnumerateArray()
                .Where(p => String(p, "name") is { Length: > 0 } && Int(p, "state") is { } state && SecurityCenterState.IsOn(state))
                .Select(p => String(p, "name")!));
        }

        return new FirewallFacts(profiles, others);
    }

    public static EncryptionFacts Encryption(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (Bool(root, "available") != true)
        {
            return new EncryptionFacts(false, EncryptionState.Unknown, false);
        }

        // Win32_EncryptableVolume.ConversionStatus: 0 decrypted, 1 encrypted, 2 encrypting, 3 decrypting, 4 encryption paused, 5 decryption paused.
        var state = Int(root, "conversionStatus") switch
        {
            0 => EncryptionState.NotEncrypted,
            1 => EncryptionState.Encrypted,
            2 or 4 => EncryptionState.Encrypting,
            3 or 5 => EncryptionState.Decrypting,
            _ => EncryptionState.Unknown,
        };

        // ProtectionStatus: 0 off, 1 on, 2 unknown (usually locked).
        return new EncryptionFacts(true, state, Int(root, "protectionStatus") == 1);
    }

    public static SecureBootTpmFacts SecureBootTpm(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var secureBoot = String(root, "secureBoot") switch
        {
            "on" => SecureBootState.On,
            "off" => SecureBootState.Off,
            "notSupported" => SecureBootState.NotSupported,
            _ => SecureBootState.Unknown,
        };

        // SpecVersion looks like "2.0, 0, 1.59". Only the first part means anything to people.
        var version = String(root, "tpmVersion")?.Split(',')[0].Trim();
        return new SecureBootTpmFacts(secureBoot, Bool(root, "tpmPresent"), Bool(root, "tpmReady"), string.IsNullOrEmpty(version) ? null : version);
    }

    public static Smb1Facts Smb1(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var feature = String(root, "feature") switch
        {
            "Enabled" or "EnablePending" => FeatureState.Enabled,
            "Disabled" or "DisablePending" => FeatureState.Disabled,
            "DisabledWithPayloadRemoved" => FeatureState.NotPresent,
            "NotPresent" => FeatureState.NotPresent,
            _ => FeatureState.Unknown,
        };
        return new Smb1Facts(feature, Bool(root, "serverSmb1"));
    }

    /// <summary>Whether this is a work or school PC, and who is signed in at the screen.</summary>
    public static Browsers.PcContext PcContext(string json, string runningAs)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var managed = Bool(root, "partOfDomain") == true || Bool(root, "azureAdJoined") == true || Bool(root, "mdmEnrolled") == true;
        return new Browsers.PcContext(managed, runningAs, String(root, "userName"));
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i) ? i : null;

    private static bool? Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        } : null;

    private static DateTimeOffset? Date(JsonElement element, string name) =>
        String(element, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
        && date.Year > 2000
            ? date
            : null;
}
