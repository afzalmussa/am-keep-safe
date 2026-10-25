using System.Diagnostics;
using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Logging;

namespace AMKeepSafe.Windows.Browsers;

/// <summary>
/// Closes browsers the polite way, as if each window's close button was clicked, so they can save
/// what they need and offer to restore tabs next time. Never forces them closed.
/// </summary>
public sealed class WindowsBrowserProcesses(IAppLog log) : IBrowserProcesses
{
    public static string ProcessName(Browser browser) => browser switch
    {
        Browser.Edge => "msedge",
        Browser.Chrome => "chrome",
        _ => "firefox",
    };

    public bool IsRunning(Browser browser)
    {
        var processes = Process.GetProcessesByName(ProcessName(browser));
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }

    public async Task<bool> CloseAsync(Browser browser, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        log.Info($"Asking {browser} to close.");
        foreach (var process in Process.GetProcessesByName(ProcessName(browser)))
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        process.CloseMainWindow();
                    }
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }
            }
        }

        var deadline = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsRunning(browser))
            {
                log.Info($"{browser} closed.");
                return true;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        log.Warning($"{browser} was still open after {wait.TotalSeconds:0} seconds, so it was left open.");
        return false;
    }
}
