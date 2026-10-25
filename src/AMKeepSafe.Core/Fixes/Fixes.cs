using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Core.Fixes;

/// <summary>Shared plumbing: read the setting, and plan a change only if it isn't already as wanted.</summary>
public abstract class FixBase(ISystemChanger changer) : IFix
{
    public abstract string Id { get; }

    public abstract IReadOnlyCollection<string> ChangeKeys { get; }

    public abstract Task<IReadOnlyList<SettingChange>> PlanAsync(FixDefinition text, CancellationToken cancellationToken);

    protected async Task<SettingChange?> IfNeededAsync(SettingChange wanted, FixDefinition text, CancellationToken cancellationToken)
    {
        var now = await changer.ReadAsync(wanted, cancellationToken).ConfigureAwait(false);
        if (string.Equals(now, wanted.To, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var words = text.TextFor(wanted.Key);
        return wanted with { From = now, Description = words.Text, UndoDescription = words.UndoText ?? "" };
    }

    protected async Task<IReadOnlyList<SettingChange>> PlanAllAsync(FixDefinition text, CancellationToken cancellationToken, params SettingChange[] wanted)
    {
        var changes = new List<SettingChange>();
        foreach (var change in wanted)
        {
            if (await IfNeededAsync(change, text, cancellationToken).ConfigureAwait(false) is { } needed)
            {
                changes.Add(needed);
            }
        }

        return changes;
    }
}

/// <summary>Turns Windows Firewall on for every kind of network where it's off.</summary>
public sealed class FirewallOnFix(ISystemChanger changer) : FixBase(changer)
{
    public const string FixId = "firewall-on";

    public override string Id => FixId;

    public override IReadOnlyCollection<string> ChangeKeys => ["domain", "private", "public"];

    public override Task<IReadOnlyList<SettingChange>> PlanAsync(FixDefinition text, CancellationToken cancellationToken) =>
        PlanAllAsync(text, cancellationToken, Profile("domain", "Domain"), Profile("private", "Private"), Profile("public", "Public"));

    private static SettingChange Profile(string key, string profile) => new()
    {
        Key = key, Kind = ChangeKind.FirewallProfile, Target = profile, To = "on",
        Undo = UndoSupport.ViaSettings, UndoPage = WindowsSettingsPage.Firewall,
    };
}

/// <summary>Switches permission prompts back on, to Windows' normal level.</summary>
public sealed class UacOnFix(ISystemChanger changer) : FixBase(changer)
{
    public const string FixId = "uac-on";

    public override string Id => FixId;

    public override IReadOnlyCollection<string> ChangeKeys => ["enableLua", "consentPrompt"];

    public override async Task<IReadOnlyList<SettingChange>> PlanAsync(FixDefinition text, CancellationToken cancellationToken)
    {
        var changes = new List<SettingChange>();

        // Missing means Windows' default, which is on, so only a 0 needs changing.
        foreach (var (key, name, value, restart) in new[] { ("enableLua", "EnableLUA", "1", true), ("consentPrompt", "ConsentPromptBehaviorAdmin", "5", false) })
        {
            var wanted = new SettingChange
            {
                Key = key, Kind = ChangeKind.RegistryValue, Root = RegistryRoot.LocalMachine, RegistryKey = UacCheck.PolicyKey,
                Target = name, To = value, NeedsRestart = restart,
                Undo = UndoSupport.ViaSettings, UndoPage = WindowsSettingsPage.UserAccountControl,
            };
            if (await IfNeededAsync(wanted, text, cancellationToken).ConfigureAwait(false) is { From: "0" } needed)
            {
                changes.Add(needed);
            }
        }

        return changes;
    }
}

/// <summary>Switches Microsoft Defender's real-time protection back on.</summary>
public sealed class DefenderRealTimeOnFix(ISystemChanger changer) : FixBase(changer)
{
    public const string FixId = "defender-real-time-on";

    public override string Id => FixId;

    public override IReadOnlyCollection<string> ChangeKeys => ["realTimeOn"];

    public override Task<IReadOnlyList<SettingChange>> PlanAsync(FixDefinition text, CancellationToken cancellationToken) =>
        PlanAllAsync(text, cancellationToken, new SettingChange
        {
            Key = "realTimeOn", Kind = ChangeKind.DefenderRealTime, Target = "RealTimeProtection", To = "on",
            Undo = UndoSupport.ViaSettings, UndoPage = WindowsSettingsPage.VirusProtection,
        });
}

/// <summary>Asks Microsoft Defender to download its latest virus information.</summary>
public sealed class UpdateVirusInfoFix : IFix
{
    public const string FixId = "defender-update";
    public const string TaskName = "UpdateVirusInfo";

    public string Id => FixId;

    public IReadOnlyCollection<string> ChangeKeys => ["update"];

    public Task<IReadOnlyList<SettingChange>> PlanAsync(FixDefinition text, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SettingChange>>(
            [new SettingChange { Key = "update", Kind = ChangeKind.Task, Target = TaskName, Undo = UndoSupport.NotNeeded, Description = text.TextFor("update").Text }]);
}

/// <summary>Runs a Microsoft Defender quick scan. Free, always (trust rule 10).</summary>
public sealed class QuickScanFix : IFix
{
    public const string FixId = "defender-quick-scan";
    public const string TaskName = "QuickScan";

    public string Id => FixId;

    public IReadOnlyCollection<string> ChangeKeys => ["scan"];

    public Task<IReadOnlyList<SettingChange>> PlanAsync(FixDefinition text, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SettingChange>>(
            [new SettingChange { Key = "scan", Kind = ChangeKind.Task, Target = TaskName, Undo = UndoSupport.NotNeeded, Description = text.TextFor("scan").Text }]);
}

/// <summary>Puts the Windows Update service back to Windows' normal start setting.</summary>
public sealed class WindowsUpdateServiceFix(ISystemChanger changer) : FixBase(changer)
{
    public const string FixId = "windows-update-service";

    public override string Id => FixId;

    public override IReadOnlyCollection<string> ChangeKeys => ["serviceManual"];

    public override Task<IReadOnlyList<SettingChange>> PlanAsync(FixDefinition text, CancellationToken cancellationToken) =>
        PlanAllAsync(text, cancellationToken, new SettingChange
        {
            Key = "serviceManual", Kind = ChangeKind.ServiceStart, Target = "wuauserv", To = "Manual",
            Undo = UndoSupport.ViaSettings, UndoPage = WindowsSettingsPage.WindowsUpdate,
        });
}

/// <summary>Turns Remote Desktop off. Can be undone in the app.</summary>
public sealed class RemoteDesktopOffFix(ISystemChanger changer) : FixBase(changer)
{
    public const string FixId = "remote-desktop-off";

    public override string Id => FixId;

    public override IReadOnlyCollection<string> ChangeKeys => ["remoteDesktopOff"];

    public override Task<IReadOnlyList<SettingChange>> PlanAsync(FixDefinition text, CancellationToken cancellationToken) =>
        PlanAllAsync(text, cancellationToken, new SettingChange
        {
            Key = "remoteDesktopOff", Kind = ChangeKind.RegistryValue, Root = RegistryRoot.LocalMachine,
            RegistryKey = RemoteDesktopCheck.TerminalServerKey, Target = "fDenyTSConnections", To = "1", Undo = UndoSupport.InApp,
        });
}

/// <summary>Switches off SMB1. Needs a restart to finish. Can be undone in the app.</summary>
public sealed class Smb1OffFix(ISystemChanger changer) : FixBase(changer)
{
    public const string FixId = "smb1-off";

    public override string Id => FixId;

    public override IReadOnlyCollection<string> ChangeKeys => ["smb1Off"];

    public override Task<IReadOnlyList<SettingChange>> PlanAsync(FixDefinition text, CancellationToken cancellationToken) =>
        PlanAllAsync(text, cancellationToken, new SettingChange
        {
            Key = "smb1Off", Kind = ChangeKind.WindowsFeature, Target = "SMB1Protocol", To = "Disabled", NeedsRestart = true, Undo = UndoSupport.InApp,
        });
}

/// <summary>Turns System Restore on for the Windows drive.</summary>
public sealed class SystemRestoreOnFix(ISystemChanger changer, string systemDrive) : FixBase(changer)
{
    public const string FixId = "system-restore-on";

    public override string Id => FixId;

    public override IReadOnlyCollection<string> ChangeKeys => ["restoreOn"];

    public override Task<IReadOnlyList<SettingChange>> PlanAsync(FixDefinition text, CancellationToken cancellationToken) =>
        PlanAllAsync(text, cancellationToken, new SettingChange
        {
            Key = "restoreOn", Kind = ChangeKind.SystemRestore, Target = systemDrive.TrimEnd('\\') + "\\", To = "on",
            Undo = UndoSupport.ViaSettings, UndoPage = WindowsSettingsPage.SystemProtection,
        });
}

public static class FixSet
{
    /// <summary>Every fix the app offers. Which result each one appears under comes from fixes.json.</summary>
    public static IReadOnlyList<IFix> Create(ISystemChanger changer, string systemDrive) =>
    [
        new FirewallOnFix(changer),
        new UacOnFix(changer),
        new DefenderRealTimeOnFix(changer),
        new UpdateVirusInfoFix(),
        new QuickScanFix(),
        new WindowsUpdateServiceFix(changer),
        new RemoteDesktopOffFix(changer),
        new Smb1OffFix(changer),
        new SystemRestoreOnFix(changer, systemDrive),
    ];
}
