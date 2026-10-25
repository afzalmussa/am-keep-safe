using System.Globalization;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Core.Fixes;

public enum ChangeKind
{
    /// <summary>A number (DWORD) in the registry. A null value means "not set".</summary>
    RegistryValue,

    /// <summary>A Windows service's start setting: Automatic, Manual or Disabled.</summary>
    ServiceStart,

    /// <summary>Windows Firewall for one kind of network: on or off.</summary>
    FirewallProfile,

    /// <summary>Microsoft Defender real-time protection: on or off.</summary>
    DefenderRealTime,

    /// <summary>An optional Windows feature: Enabled or Disabled.</summary>
    WindowsFeature,

    /// <summary>System Restore on a drive: on or off.</summary>
    SystemRestore,

    /// <summary>A whole registry key and everything in it: present or absent. Backed up before it's removed.</summary>
    RegistryKey,

    /// <summary>A file: present or absent. Backed up before it's removed.</summary>
    File,

    /// <summary>Something run once, such as a virus scan. Changes no setting.</summary>
    Task,
}

/// <summary>How a change can be reversed.</summary>
public enum UndoSupport
{
    /// <summary>From the app's "Undo changes" list.</summary>
    InApp,

    /// <summary>
    /// The change switched a protection on. Turning it off again would break trust rule 6, so the app
    /// explains and opens the Windows Settings page instead.
    /// </summary>
    ViaSettings,

    /// <summary>Nothing to undo, for example a virus scan.</summary>
    NotNeeded,
}

/// <summary>
/// One exact change to one Windows setting, from one value to another. This is what the preview
/// shows, what the undo journal records and what the Windows project applies.
/// </summary>
public sealed record SettingChange
{
    /// <summary>Which wording in fixes.json describes this change.</summary>
    public required string Key { get; init; }

    public required ChangeKind Kind { get; init; }

    /// <summary>What's being changed: a profile, service, feature, drive or task name. For registry values, the value name.</summary>
    public required string Target { get; init; }

    public RegistryRoot? Root { get; init; }

    public string? RegistryKey { get; init; }

    /// <summary>The value now, in the same words as <see cref="To"/>. Null means "not set" or unknown.</summary>
    public string? From { get; init; }

    /// <summary>The value after the change. For registry values, null means the value is removed.</summary>
    public string? To { get; init; }

    /// <summary>What the change does, in everyday words. From fixes.json.</summary>
    public string Description { get; init; } = "";

    /// <summary>What undoing it does, in everyday words. From fixes.json.</summary>
    public string UndoDescription { get; init; } = "";

    public UndoSupport Undo { get; init; }

    /// <summary>For <see cref="UndoSupport.ViaSettings"/>: where the person can change it themselves.</summary>
    public WindowsSettingsPage? UndoPage { get; init; }

    public bool NeedsRestart { get; init; }

    /// <summary>For a key or file being removed: what's in it, shown in the preview.</summary>
    public IReadOnlyList<string> Contents { get; init; } = [];

    /// <summary>Where the copy taken before removal is kept. Undo restores from this file (trust rule 5).</summary>
    public string? BackupFile { get; init; }

    public const string Present = "present";
    public const string Absent = "absent";

    /// <summary>True for removing a key or file, which must be backed up first.</summary>
    public bool IsRemoval => Kind is ChangeKind.RegistryKey or ChangeKind.File && To == Absent;

    /// <summary>The exact change in technical terms, shown under "Exactly what changes" and written to the log.</summary>
    public string TechnicalDetail => Kind switch
    {
        ChangeKind.RegistryValue => $@"Registry value {(Root == RegistryRoot.CurrentUser ? "HKCU" : "HKLM")}\{RegistryKey}\{Target}: from {From ?? "not set"} to {To ?? "not set"}",
        ChangeKind.ServiceStart => $"Windows service \"{Target}\" start setting: from {From ?? "unknown"} to {To}",
        ChangeKind.FirewallProfile => $"Windows Firewall, {Target} networks: from {From ?? "unknown"} to {To}",
        ChangeKind.DefenderRealTime => $"Microsoft Defender real-time protection: from {From ?? "unknown"} to {To}",
        ChangeKind.WindowsFeature => $"Windows optional feature \"{Target}\": from {From ?? "unknown"} to {To}",
        ChangeKind.SystemRestore => $"System Restore on drive {Target}: from {From ?? "unknown"} to {To}",
        ChangeKind.RegistryKey => $@"Registry key {(Root == RegistryRoot.CurrentUser ? "HKCU" : "HKLM")}\{RegistryKey} and everything in it: from {From ?? "unknown"} to {To}{ContentsText}",
        ChangeKind.File => $"File {Target}: from {From ?? "unknown"} to {To}{ContentsText}",
        ChangeKind.Task => $"Runs \"{Target}\" once. No settings change.",
        _ => $"{Kind} {Target}: from {From} to {To}",
    };

    private string ContentsText => Contents.Count == 0 ? "" : $". It holds: {string.Join("; ", Contents)}";

    /// <summary>The change that puts things back. The description becomes the undo wording.</summary>
    public SettingChange Reversed() => this with
    {
        From = To,
        To = From,
        Description = UndoDescription,
        UndoDescription = Description,
    };

    /// <summary>Everything needed to undo this later, for the undo journal.</summary>
    public IReadOnlyDictionary<string, string> ToUndoData()
    {
        var data = new Dictionary<string, string>
        {
            ["key"] = Key,
            ["kind"] = Kind.ToString(),
            ["target"] = Target,
            ["undo"] = Undo.ToString(),
            ["description"] = Description,
            ["undoDescription"] = UndoDescription,
            ["needsRestart"] = NeedsRestart.ToString(CultureInfo.InvariantCulture),
        };
        Add(data, "root", Root?.ToString());
        Add(data, "registryKey", RegistryKey);
        Add(data, "from", From);
        Add(data, "to", To);
        Add(data, "undoPage", UndoPage?.ToString());
        Add(data, "backupFile", BackupFile);
        if (Contents.Count > 0)
        {
            data["contents"] = string.Join("\n", Contents);
        }

        return data;
    }

    /// <summary>Rebuilds a change from the undo journal. Null if the record is damaged.</summary>
    public static SettingChange? FromUndoData(IReadOnlyDictionary<string, string> data)
    {
        if (!data.TryGetValue("key", out var key)
            || !data.TryGetValue("target", out var target)
            || !Enum.TryParse<ChangeKind>(data.GetValueOrDefault("kind"), out var kind)
            || !Enum.TryParse<UndoSupport>(data.GetValueOrDefault("undo"), out var undo))
        {
            return null;
        }

        return new SettingChange
        {
            Key = key,
            Kind = kind,
            Target = target,
            Undo = undo,
            Root = Enum.TryParse<RegistryRoot>(data.GetValueOrDefault("root"), out var root) ? root : null,
            RegistryKey = data.GetValueOrDefault("registryKey"),
            From = data.GetValueOrDefault("from"),
            To = data.GetValueOrDefault("to"),
            Description = data.GetValueOrDefault("description") ?? "",
            UndoDescription = data.GetValueOrDefault("undoDescription") ?? "",
            UndoPage = Enum.TryParse<WindowsSettingsPage>(data.GetValueOrDefault("undoPage"), out var page) ? page : null,
            NeedsRestart = bool.TryParse(data.GetValueOrDefault("needsRestart"), out var restart) && restart,
            BackupFile = data.GetValueOrDefault("backupFile"),
            Contents = data.GetValueOrDefault("contents")?.Split('\n') ?? [],
        };
    }

    private static void Add(Dictionary<string, string> data, string name, string? value)
    {
        if (value is not null)
        {
            data[name] = value;
        }
    }
}
