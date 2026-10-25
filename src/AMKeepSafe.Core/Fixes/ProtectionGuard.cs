using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Core.Fixes;

/// <summary>
/// Trust rule 6: no feature turns off Defender, the firewall, User Account Control, Windows Update
/// or any other protection, even temporarily. Every change goes through this check before it's
/// made, including undo, so the rule holds whatever the screens do.
/// </summary>
public static class ProtectionGuard
{
    private const string UacKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

    // Registry values that switch a protection off, and the value that does it.
    private static readonly (string Key, string Name, string WeakValue)[] WeakeningRegistryValues =
    [
        (UacKey, "EnableLUA", "0"),
        (UacKey, "ConsentPromptBehaviorAdmin", "0"),
        (UacKey, "PromptOnSecureDesktop", "0"),
        (@"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware", "1"),
        (@"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiVirus", "1"),
        (@"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring", "1"),
        (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", "1"),
        (@"SOFTWARE\Policies\Microsoft\WindowsFirewall\DomainProfile", "EnableFirewall", "0"),
        (@"SOFTWARE\Policies\Microsoft\WindowsFirewall\StandardProfile", "EnableFirewall", "0"),
        (@"SOFTWARE\Policies\Microsoft\WindowsFirewall\PublicProfile", "EnableFirewall", "0"),
        (@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR", "1"),
    ];

    // Services that keep the PC protected or up to date.
    private static readonly string[] ProtectionServices = ["wuauserv", "WinDefend", "mpssvc", "BFE", "wscsvc", "UsoSvc", "WaaSMedicSvc"];

    /// <summary>
    /// Whole keys and files the app may remove or restore: the browser policy locations only. Anything
    /// else is refused, so the power to remove a key can never reach beyond what it was built for.
    /// </summary>
    private static readonly string[] RemovableKeys =
    [
        @"SOFTWARE\Policies\Microsoft\Edge",
        @"SOFTWARE\Policies\Google\Chrome",
        @"SOFTWARE\Policies\Mozilla\Firefox",
    ];

    /// <summary>The one check every change passes before it's made, including undo.</summary>
    public static bool Allows(SettingChange change) => !WouldWeaken(change) && change.Kind switch
    {
        ChangeKind.RegistryKey => RemovableKeys.Contains(change.RegistryKey, StringComparer.OrdinalIgnoreCase),
        ChangeKind.File => IsFirefoxPolicyFile(change.Target),
        _ => true,
    };

    private static bool IsFirefoxPolicyFile(string path)
    {
        var normalised = path.Replace('/', '\\');
        return normalised.EndsWith(@"\distribution\policies.json", StringComparison.OrdinalIgnoreCase)
               && normalised.Contains(@"\Mozilla Firefox\", StringComparison.OrdinalIgnoreCase)
               && !normalised.Contains("..", StringComparison.Ordinal);
    }

    /// <summary>True if the change would switch off, or weaken, a protection.</summary>
    public static bool WouldWeaken(SettingChange change) => change.Kind switch
    {
        ChangeKind.FirewallProfile => IsOff(change.To),
        ChangeKind.DefenderRealTime => IsOff(change.To),
        ChangeKind.SystemRestore => IsOff(change.To),
        ChangeKind.ServiceStart => ProtectionServices.Contains(change.Target, StringComparer.OrdinalIgnoreCase)
                                   && string.Equals(change.To, "Disabled", StringComparison.OrdinalIgnoreCase),
        ChangeKind.RegistryValue => change.Root == RegistryRoot.LocalMachine && WeakeningRegistryValues.Any(v =>
            string.Equals(v.Key, change.RegistryKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(v.Name, change.Target, StringComparison.OrdinalIgnoreCase)
            && v.WeakValue == change.To),
        _ => false,
    };

    private static bool IsOff(string? value) => !string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
}
