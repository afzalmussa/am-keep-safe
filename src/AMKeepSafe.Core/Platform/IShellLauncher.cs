using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Checks;

namespace AMKeepSafe.Core.Platform;

/// <summary>Opens web pages, files, folders and Windows settings with whatever Windows normally uses for them.</summary>
public interface IShellLauncher
{
    /// <summary>Opens an https address in the default browser. Returns false if it couldn't.</summary>
    bool OpenUrl(string url);

    /// <summary>Opens a file with its default program (a log file opens in Notepad, for example).</summary>
    bool OpenFile(string path);

    bool OpenFolder(string path);

    /// <summary>Opens one of a fixed list of Windows settings pages.</summary>
    bool OpenSettings(WindowsSettingsPage page);

    /// <summary>Opens one of the browser's own pages, such as chrome://extensions, in that browser.</summary>
    bool OpenBrowserPage(Browser browser, string address);
}
