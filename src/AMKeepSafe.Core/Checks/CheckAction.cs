namespace AMKeepSafe.Core.Checks;

/// <summary>
/// Windows pages the app can open for the user. A fixed list, so a data file can't make the app
/// launch anything else.
/// </summary>
public enum WindowsSettingsPage
{
    WindowsUpdate,
    VirusProtection,
    Firewall,
    DeviceSecurity,
    DeviceEncryption,
    RemoteDesktop,
    Backup,
    SystemProtection,
    InstalledApps,
    UserAccountControl,
    WindowsFeatures,
}

/// <summary>A button shown under a result: opens a Windows settings page or an https web page.</summary>
public sealed record CheckAction(string Label, WindowsSettingsPage? SettingsPage, string? Url);
