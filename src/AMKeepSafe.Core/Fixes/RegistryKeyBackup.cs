using System.Text.Json;
using System.Text.Json.Serialization;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Core.Fixes;

/// <summary>One saved registry value. Exactly as it was, including its type, so it can be put back exactly.</summary>
/// <param name="Kind">String, ExpandString, MultiString, DWord, QWord or Binary.</param>
/// <param name="Data">The value as text. Numbers in decimal, binary as base64.</param>
/// <param name="Strings">For MultiString values.</param>
public sealed record RegistryValueBackup(string Name, string Kind, string? Data, IReadOnlyList<string>? Strings = null);

/// <summary>
/// A copy of a registry key and everything under it, saved as readable JSON before the key is
/// removed. Undo puts it back from this file.
/// </summary>
public sealed record RegistryKeyBackup
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public RegistryRoot Root { get; init; }

    /// <summary>The key's full path for the top key, its own name for keys under it.</summary>
    public required string Path { get; init; }

    public IReadOnlyList<RegistryValueBackup> Values { get; init; } = [];

    public IReadOnlyList<RegistryKeyBackup> SubKeys { get; init; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static RegistryKeyBackup FromJson(string json) =>
        JsonSerializer.Deserialize<RegistryKeyBackup>(json, Options) ?? throw new InvalidDataException("The backup file is empty.");

    /// <summary>A file name for a copy: the time, then a readable form of what was copied.</summary>
    public static string FileNameFor(DateTimeOffset when, string what)
    {
        var safe = new string(what.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (safe.Contains("--", StringComparison.Ordinal))
        {
            safe = safe.Replace("--", "-", StringComparison.Ordinal);
        }

        return $"{when.ToLocalTime():yyyyMMdd-HHmmss}-{(safe.Length > 80 ? safe[..80] : safe)}";
    }
}
