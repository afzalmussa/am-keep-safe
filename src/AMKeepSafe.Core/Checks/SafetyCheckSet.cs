using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Core.Checks;

/// <summary>Where the checks get their facts from: Windows in the app, fakes in the tests.</summary>
public sealed record CheckSources(
    IRegistryReader Registry,
    IProcessList Processes,
    IWindowsUpdateInfo WindowsUpdate,
    IAntivirusInfo Antivirus,
    IFirewallInfo Firewall,
    IEncryptionInfo Encryption,
    ISecureBootTpmInfo SecureBootTpm,
    ISmb1Info Smb1,
    RemoteToolList RemoteTools,
    TimeProvider Time);

public static class SafetyCheckSet
{
    /// <summary>Every check on the Safety check tab. The order of results comes from checks.json.</summary>
    public static IReadOnlyList<ISafetyCheck> Create(CheckSources sources) =>
    [
        new WindowsUpdateCheck(sources.WindowsUpdate, sources.Time),
        new WindowsVersionCheck(sources.Registry, sources.WindowsUpdate, sources.Time),
        new AntivirusCheck(sources.Antivirus, sources.Time),
        new FirewallCheck(sources.Firewall),
        new UacCheck(sources.Registry),
        new EncryptionCheck(sources.Encryption),
        new BackupCheck(sources.Registry),
        new RemoteDesktopCheck(sources.Registry),
        new Smb1Check(sources.Smb1),
        new RemoteToolsCheck(sources.RemoteTools, sources.Registry, sources.Processes),
        new SecureBootTpmCheck(sources.SecureBootTpm),
    ];
}
