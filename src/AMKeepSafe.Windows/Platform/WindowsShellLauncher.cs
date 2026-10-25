using System.Diagnostics;
using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Configuration;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Windows.Platform;

public sealed class WindowsShellLauncher(IAppLog log) : IShellLauncher
{
    public bool OpenUrl(string url)
    {
        if (!AppSettings.IsUsableUrl(url))
        {
            log.Warning($"Refused to open '{url}' because it is not a secure (https) web address.");
            return false;
        }

        return Start(new ProcessStartInfo(url) { UseShellExecute = true }, $"web page {url}");
    }

    public bool OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            log.Warning($"Could not open '{path}' because the file does not exist.");
            return false;
        }

        return Start(new ProcessStartInfo(path) { UseShellExecute = true }, $"file {path}");
    }

    public bool OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning($"Could not create folder '{path}'.", ex);
            return false;
        }

        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var info = new ProcessStartInfo(explorer) { UseShellExecute = false };
        info.ArgumentList.Add(path);
        return Start(info, $"folder {path}");
    }

    public bool OpenSettings(WindowsSettingsPage page)
    {
        var (target, isProgram) = SettingsTarget(page);
        var info = isProgram
            ? new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), target)) { UseShellExecute = false }
            : new ProcessStartInfo(target) { UseShellExecute = true };
        return Start(info, $"Windows settings page {page}");
    }

    public bool OpenBrowserPage(Browser browser, string address)
    {
        var allowed = browser switch { Browser.Chrome => "chrome://", Browser.Edge => "edge://", _ => "about:" };
        if (!address.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || address.Any(char.IsWhiteSpace))
        {
            log.Warning($"Refused to open '{address}' in {browser}: not one of its own pages.");
            return false;
        }

        if (BrowserExe(browser) is not { } exe)
        {
            log.Warning($"Couldn't find {browser} to open {address}.");
            return false;
        }

        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        info.ArgumentList.Add(address);
        return Start(info, $"{address} in {browser}");
    }

    /// <summary>Where the browser is installed, from Windows' list of app paths.</summary>
    private static string? BrowserExe(Browser browser)
    {
        var exe = browser switch { Browser.Edge => "msedge.exe", Browser.Chrome => "chrome.exe", _ => "firefox.exe" };
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (key?.GetValue(null) is string path && File.Exists(path.Trim('"')))
            {
                return path.Trim('"');
            }
        }

        return null;
    }

    /// <summary>Where each page lives: a settings address, or a program in the Windows system folder.</summary>
    public static (string Target, bool IsProgram) SettingsTarget(WindowsSettingsPage page) => page switch
    {
        WindowsSettingsPage.WindowsUpdate => ("ms-settings:windowsupdate", false),
        WindowsSettingsPage.VirusProtection => ("windowsdefender://threat", false),
        WindowsSettingsPage.Firewall => ("windowsdefender://network", false),
        WindowsSettingsPage.DeviceSecurity => ("windowsdefender://devicesecurity", false),
        WindowsSettingsPage.DeviceEncryption => ("ms-settings:deviceencryption", false),
        WindowsSettingsPage.RemoteDesktop => ("ms-settings:remotedesktop", false),
        WindowsSettingsPage.Backup => ("ms-settings:backup", false),
        WindowsSettingsPage.InstalledApps => ("ms-settings:appsfeatures", false),
        WindowsSettingsPage.SystemProtection => ("SystemPropertiesProtection.exe", true),
        WindowsSettingsPage.UserAccountControl => ("UserAccountControlSettings.exe", true),
        WindowsSettingsPage.WindowsFeatures => ("OptionalFeatures.exe", true),
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
    };

    private bool Start(ProcessStartInfo info, string what)
    {
        try
        {
            using var _ = Process.Start(info);
            log.Info($"Opened {what}.");
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            log.Error($"Could not open {what}.", ex);
            return false;
        }
    }
}
