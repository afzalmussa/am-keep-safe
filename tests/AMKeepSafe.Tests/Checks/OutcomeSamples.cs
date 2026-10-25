using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Checks;

/// <summary>
/// For every check, facts that produce each of its outcomes, run through the real rules. Used to
/// prove every outcome has wording and every {placeholder} in that wording gets filled in.
/// </summary>
public static class OutcomeSamples
{
    private static readonly DateTimeOffset Now = HealthyPc.Now;

    public static IReadOnlyDictionary<string, IReadOnlyList<CheckOutcome>> All(CheckCatalog catalog)
    {
        T Settings<T>(string id) where T : new() => catalog[id].ParametersAs<T>();

        var update = Settings<WindowsUpdateCheck.Settings>(WindowsUpdateCheck.CheckId);
        WindowsUpdateStatus Status(ServiceStart s = ServiceStart.Manual, double? days = 5, bool off = false, DateTimeOffset? paused = null) =>
            new(s, days is { } d ? Now.AddDays(-d) : null, off, paused);

        var version = Settings<WindowsVersionCheck.Settings>(WindowsVersionCheck.CheckId);
        CheckOutcome Version(int build, string edition = "Core", double? days = 5, DateTimeOffset? now = null, string product = "Windows 10 Home") =>
            WindowsVersionCheck.Evaluate(new WindowsVersionFacts(product, edition, "25H2", build), days is { } d ? Now.AddDays(-d) : null, now ?? Now, version);

        var antivirus = Settings<AntivirusCheck.Settings>(AntivirusCheck.CheckId);
        CheckOutcome Av(IReadOnlyList<AntivirusProduct>? products, DefenderStatus? defender) =>
            AntivirusCheck.Evaluate(new AntivirusFacts(products, defender), Now, antivirus);
        var defenderOn = new AntivirusProduct("Windows Defender", true, true);

        var firewall = Settings<FirewallCheck.Settings>(FirewallCheck.CheckId);
        CheckOutcome Fw(bool d, bool p, bool pub, params string[] others) => FirewallCheck.Evaluate(
            new FirewallFacts(new Dictionary<NetworkProfile, bool> { [NetworkProfile.Domain] = d, [NetworkProfile.Private] = p, [NetworkProfile.Public] = pub }, others), firewall);

        var backup = Settings<BackupCheck.Settings>(BackupCheck.CheckId);
        var tools = Settings<RemoteToolsCheck.Settings>(RemoteToolsCheck.CheckId);

        return new Dictionary<string, IReadOnlyList<CheckOutcome>>
        {
            [WindowsUpdateCheck.CheckId] =
            [
                WindowsUpdateCheck.Evaluate(Status(), 0, Now, update),
                WindowsUpdateCheck.Evaluate(Status(), 2, Now, update),
                WindowsUpdateCheck.Evaluate(Status(days: 60), 0, Now, update),
                WindowsUpdateCheck.Evaluate(Status(days: 200), 0, Now, update),
                WindowsUpdateCheck.Evaluate(Status(paused: Now.AddDays(7)), 0, Now, update),
                WindowsUpdateCheck.Evaluate(Status(off: true), 0, Now, update),
                WindowsUpdateCheck.Evaluate(Status(ServiceStart.Disabled, days: null), null, Now, update),
                WindowsUpdateCheck.Evaluate(Status(ServiceStart.Disabled), null, Now, update),
            ],
            [WindowsVersionCheck.CheckId] =
            [
                Version(26200),
                Version(28000),
                Version(26200, now: new DateTimeOffset(2027, 9, 1, 0, 0, 0, TimeSpan.Zero)),
                Version(26100),
                Version(19045, days: 5),
                Version(19045, days: 100),
                Version(19045, days: null),
                Version(19041),
                Version(19045, now: new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                Version(19045, edition: "Enterprise"),
                Version(19044, edition: "EnterpriseS", product: "Windows 10 Enterprise LTSC 2021"),
                Version(9600),
            ],
            [AntivirusCheck.CheckId] =
            [
                Av([defenderOn], new DefenderStatus(true, true, Now.AddHours(-3), "Normal")),
                Av([defenderOn], new DefenderStatus(true, true, null, "Normal")),
                Av([defenderOn], new DefenderStatus(true, true, Now.AddDays(-5), "Normal")),
                Av([defenderOn], new DefenderStatus(true, true, Now.AddDays(-30), "Normal")),
                Av([defenderOn], new DefenderStatus(true, false, Now, "Normal")),
                Av([new AntivirusProduct("Norton 360", true, true)], null),
                Av([new AntivirusProduct("McAfee", true, false)], null),
                Av([], new DefenderStatus(false, false, null, null)),
            ],
            [FirewallCheck.CheckId] = [Fw(true, true, true), Fw(false, false, false, "ESET"), Fw(true, true, false), Fw(false, false, false)],
            [UacCheck.CheckId] = [UacCheck.Evaluate(1, 5), UacCheck.Evaluate(1, 0), UacCheck.Evaluate(0, 5)],
            [EncryptionCheck.CheckId] =
            [
                EncryptionCheck.Evaluate(new EncryptionFacts(true, EncryptionState.Encrypted, true)),
                EncryptionCheck.Evaluate(new EncryptionFacts(true, EncryptionState.Encrypted, false)),
                EncryptionCheck.Evaluate(new EncryptionFacts(true, EncryptionState.Encrypting, false)),
                EncryptionCheck.Evaluate(new EncryptionFacts(true, EncryptionState.NotEncrypted, false)),
                EncryptionCheck.Evaluate(new EncryptionFacts(false, EncryptionState.Unknown, false)),
            ],
            [BackupCheck.CheckId] =
            [
                BackupCheck.Evaluate(new BackupFacts(true, ["Desktop"], true), backup),
                BackupCheck.Evaluate(new BackupFacts(false, ["Documents"], false), backup),
                BackupCheck.Evaluate(new BackupFacts(false, [], true), backup),
                BackupCheck.Evaluate(new BackupFacts(false, [], false), backup),
            ],
            [RemoteDesktopCheck.CheckId] = [RemoteDesktopCheck.Evaluate(1, false), RemoteDesktopCheck.Evaluate(0, false)],
            [Smb1Check.CheckId] = [Smb1Check.Evaluate(new Smb1Facts(FeatureState.Disabled, false)), Smb1Check.Evaluate(new Smb1Facts(FeatureState.Enabled, true))],
            [RemoteToolsCheck.CheckId] =
            [
                RemoteToolsCheck.Evaluate([], tools),
                RemoteToolsCheck.Evaluate([new FoundRemoteTool("AnyDesk", true, true)], tools),
            ],
            [SecureBootTpmCheck.CheckId] =
            [
                SecureBootTpmCheck.Evaluate(new SecureBootTpmFacts(SecureBootState.On, true, true, "2.0")),
                SecureBootTpmCheck.Evaluate(new SecureBootTpmFacts(SecureBootState.Off, true, true, null)),
                SecureBootTpmCheck.Evaluate(new SecureBootTpmFacts(SecureBootState.On, false, null, null)),
                SecureBootTpmCheck.Evaluate(new SecureBootTpmFacts(SecureBootState.NotSupported, false, null, null)),
            ],
        };
    }
}
