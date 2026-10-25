using AMKeepSafe.Core.Platform;
using Microsoft.Win32;

namespace AMKeepSafe.Windows.Platform;

/// <summary>Reads the registry. Opens every key read-only.</summary>
public sealed class WindowsRegistryReader : IRegistryReader
{
    public object? GetValue(RegistryRoot root, string key, string name, bool use32BitView = false)
    {
        try
        {
            using var opened = Open(root, key, use32BitView);
            return opened?.GetValue(name);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string key, bool use32BitView = false)
    {
        try
        {
            using var opened = Open(root, key, use32BitView);
            return opened?.GetSubKeyNames() ?? [];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    public IReadOnlyList<string> GetValueNames(RegistryRoot root, string key, bool use32BitView = false)
    {
        try
        {
            using var opened = Open(root, key, use32BitView);
            return opened?.GetValueNames() ?? [];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static RegistryKey? Open(RegistryRoot root, string key, bool use32BitView)
    {
        var hive = root == RegistryRoot.LocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        using var baseKey = RegistryKey.OpenBaseKey(hive, use32BitView ? RegistryView.Registry32 : RegistryView.Registry64);
        return baseKey.OpenSubKey(key, writable: false);
    }
}
