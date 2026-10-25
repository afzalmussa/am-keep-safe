using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Tests.Fixes;

/// <summary>Trust rule 6: nothing turns off Defender, the firewall, UAC, Windows Update or any other protection.</summary>
public class ProtectionGuardTests
{
    private static SettingChange Change(ChangeKind kind, string target, string? to, string? key = null) =>
        new() { Key = "k", Kind = kind, Target = target, To = to, Root = key is null ? null : RegistryRoot.LocalMachine, RegistryKey = key };

    public static TheoryData<SettingChange> Weakening() =>
    [
        Change(ChangeKind.FirewallProfile, "Public", "off"),
        Change(ChangeKind.FirewallProfile, "Public", null),
        Change(ChangeKind.DefenderRealTime, "RealTimeProtection", "off"),
        Change(ChangeKind.SystemRestore, @"C:\", "off"),
        Change(ChangeKind.ServiceStart, "wuauserv", "Disabled"),
        Change(ChangeKind.ServiceStart, "WinDefend", "Disabled"),
        Change(ChangeKind.ServiceStart, "MpsSvc", "disabled"),
        Change(ChangeKind.RegistryValue, "EnableLUA", "0", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"),
        Change(ChangeKind.RegistryValue, "ConsentPromptBehaviorAdmin", "0", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"),
        Change(ChangeKind.RegistryValue, "NoAutoUpdate", "1", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU"),
        Change(ChangeKind.RegistryValue, "DisableRealtimeMonitoring", "1", @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection"),
        Change(ChangeKind.RegistryValue, "EnableFirewall", "0", @"software\policies\microsoft\windowsfirewall\publicprofile"),
    ];

    public static TheoryData<SettingChange> Fine() =>
    [
        Change(ChangeKind.FirewallProfile, "Public", "on"),
        Change(ChangeKind.DefenderRealTime, "RealTimeProtection", "on"),
        Change(ChangeKind.SystemRestore, @"C:\", "on"),
        Change(ChangeKind.ServiceStart, "wuauserv", "Manual"),
        Change(ChangeKind.ServiceStart, "Spooler", "Disabled"),
        Change(ChangeKind.RegistryValue, "EnableLUA", "1", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"),
        Change(ChangeKind.RegistryValue, "fDenyTSConnections", "1", @"SYSTEM\CurrentControlSet\Control\Terminal Server"),
        // Agreed with the owner: undoing "Remote Desktop off" and "SMB1 off" is allowed in the app.
        Change(ChangeKind.RegistryValue, "fDenyTSConnections", "0", @"SYSTEM\CurrentControlSet\Control\Terminal Server"),
        Change(ChangeKind.WindowsFeature, "SMB1Protocol", "Enabled"),
        Change(ChangeKind.Task, "QuickScan", null),
    ];

    [Theory]
    [MemberData(nameof(Weakening))]
    public void Blocks_changes_that_switch_protection_off(SettingChange change)
    {
        Assert.True(ProtectionGuard.WouldWeaken(change), change.TechnicalDetail);
    }

    [Theory]
    [MemberData(nameof(Fine))]
    public void Allows_changes_that_dont(SettingChange change)
    {
        Assert.False(ProtectionGuard.WouldWeaken(change), change.TechnicalDetail);
    }

    [Fact]
    public void Only_the_local_machine_policy_counts_so_a_same_named_user_value_isnt_mistaken_for_it()
    {
        var change = Change(ChangeKind.RegistryValue, "EnableLUA", "0", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System") with { Root = RegistryRoot.CurrentUser };

        Assert.False(ProtectionGuard.WouldWeaken(change));
    }
}
