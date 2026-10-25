namespace AMKeepSafe.Core.Browsers;

/// <summary>Whether a browser is open, and asking it to close. Never forces a browser closed, so it can save what it needs to.</summary>
public interface IBrowserProcesses
{
    bool IsRunning(Browser browser);

    /// <summary>Asks every window of the browser to close, then waits. True if it closed.</summary>
    Task<bool> CloseAsync(Browser browser, TimeSpan wait, CancellationToken cancellationToken = default);
}
