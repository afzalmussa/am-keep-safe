using AMKeepSafe.Core.Browsers;

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

/// <summary>A browser's own settings pages that the app can open. The addresses are in data/browser-check.json.</summary>
public enum BrowserPage
{
    Extensions,
    Notifications,
    Search,
    StartPages,
    Reset,
}

/// <summary>Tabs in the app a result can point to.</summary>
public enum AppTab
{
    SafetyCheck,
    BrowserCheck,
    ScamHelp,
    Pro,
}

/// <summary>
/// A button shown under a result. It does exactly one thing: open a Windows settings page, a secure
/// web page, one of a browser's own settings pages, or another tab in the app.
/// </summary>
/// <param name="Browser">For <see cref="BrowserPage"/>: which browser. Filled in by the app, not the data file.</param>
public sealed record CheckAction(
    string Label,
    WindowsSettingsPage? SettingsPage = null,
    string? Url = null,
    BrowserPage? BrowserPage = null,
    AppTab? AppTab = null,
    Browser? Browser = null)
{
    /// <summary>How many of the things a button can do are set. Exactly one is allowed.</summary>
    public int TargetCount => (SettingsPage is null ? 0 : 1) + (Url is null ? 0 : 1) + (BrowserPage is null ? 0 : 1) + (AppTab is null ? 0 : 1);
}
