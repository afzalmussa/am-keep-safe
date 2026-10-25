namespace AMKeepSafe.Core.Platform;

public enum RegistryRoot
{
    LocalMachine,
    CurrentUser,
}

/// <summary>
/// Reads Windows settings from the registry. Read only: nothing here can change a setting.
/// Always reads the 64-bit view unless asked otherwise, so a setting isn't missed because of
/// where Windows keeps 32-bit programs' copies.
/// </summary>
public interface IRegistryReader
{
    /// <summary>The value, or null if the key or value doesn't exist or can't be read. DWORDs come back as int, strings as string.</summary>
    object? GetValue(RegistryRoot root, string key, string name, bool use32BitView = false);

    /// <summary>The names of the keys directly under <paramref name="key"/>. Empty if it doesn't exist.</summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string key, bool use32BitView = false);

    /// <summary>The names of the values in <paramref name="key"/>. Empty if it doesn't exist.</summary>
    IReadOnlyList<string> GetValueNames(RegistryRoot root, string key, bool use32BitView = false);
}

public static class RegistryReaderExtensions
{
    public static int? GetInt(this IRegistryReader registry, RegistryRoot root, string key, string name) =>
        registry.GetValue(root, key, name) switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null,
        };

    public static string? GetString(this IRegistryReader registry, RegistryRoot root, string key, string name, bool use32BitView = false) =>
        registry.GetValue(root, key, name, use32BitView) as string;
}
