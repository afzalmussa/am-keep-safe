using AMKeepSafe.Core.Text;

namespace AMKeepSafe.Core.Checks.Rules;

public enum ServiceStart
{
    Automatic,
    Manual,
    Disabled,
    Unknown,
}

/// <param name="ServiceStart">The Windows Update service's start setting. Manual is normal: Windows starts it when needed.</param>
/// <param name="LastInstalled">When updates last installed successfully, if Windows says.</param>
/// <param name="TurnedOffByPolicy">Automatic updates switched off with a policy setting.</param>
/// <param name="PausedUntil">Set when someone has paused updates.</param>
public sealed record WindowsUpdateStatus(ServiceStart ServiceStart, DateTimeOffset? LastInstalled, bool TurnedOffByPolicy, DateTimeOffset? PausedUntil);

public interface IWindowsUpdateInfo
{
    /// <summary>Quick: reads settings and history only.</summary>
    Task<WindowsUpdateStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Slow: asks Windows Update what's waiting, which can go online. Null if it couldn't tell.</summary>
    Task<int?> CountWaitingUpdatesAsync(CancellationToken cancellationToken);
}

public sealed class WindowsUpdateCheck(IWindowsUpdateInfo info, TimeProvider time) : ISafetyCheck
{
    public const string CheckId = "windows-update";

    public const string Working = "working";
    public const string WorkingWithWaiting = "workingWithWaiting";
    public const string AWhile = "aWhile";
    public const string NotForAWhile = "notForAWhile";
    public const string Paused = "paused";
    public const string TurnedOff = "turnedOff";
    public const string ServiceDisabled = "serviceDisabled";

    public sealed record Settings
    {
        public int AmberAfterDays { get; init; } = 45;

        public int RedAfterDays { get; init; } = 90;
    }

    public string Id => CheckId;

    public IReadOnlyCollection<string> OutcomeCodes => [Working, WorkingWithWaiting, AWhile, NotForAWhile, Paused, TurnedOff, ServiceDisabled];

    public async Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken)
    {
        var status = await info.GetStatusAsync(cancellationToken).ConfigureAwait(false);

        // Only worth the slow lookup if the answer could change what we say.
        int? waiting = null;
        if (status.ServiceStart != ServiceStart.Disabled)
        {
            waiting = await info.CountWaitingUpdatesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Evaluate(status, waiting, time.GetUtcNow(), definition.ParametersAs<Settings>());
    }

    public static CheckOutcome Evaluate(WindowsUpdateStatus status, int? waiting, DateTimeOffset now, Settings settings)
    {
        var detail = $"Service start: {status.ServiceStart}. Last installed: {status.LastInstalled?.ToString("u") ?? "unknown"}. " +
                     $"Waiting: {waiting?.ToString() ?? "unknown"}. Turned off by policy: {status.TurnedOffByPolicy}. Paused until: {status.PausedUntil?.ToString("u") ?? "not paused"}.";
        var values = new Dictionary<string, string>();
        if (status.LastInstalled is { } last)
        {
            values["lastInstalledDate"] = FriendlyText.Date(last);
            values["lastInstalledAgo"] = FriendlyText.Ago(last, now);
        }

        if (status.ServiceStart == ServiceStart.Disabled)
        {
            return new CheckOutcome(ServiceDisabled, values, Detail: detail);
        }

        if (status.TurnedOffByPolicy)
        {
            return new CheckOutcome(TurnedOff, values, Detail: detail);
        }

        if (status.PausedUntil is { } until && until > now)
        {
            values["pausedUntil"] = FriendlyText.Date(until);
            return new CheckOutcome(Paused, values, Detail: detail);
        }

        if (status.LastInstalled is null)
        {
            return new CheckOutcome(CheckCatalog.CouldNotCheckCode, Detail: detail + " Windows didn't say when updates last installed.");
        }

        var days = (now - status.LastInstalled.Value).TotalDays;
        if (days > settings.RedAfterDays)
        {
            return new CheckOutcome(NotForAWhile, values, Detail: detail);
        }

        if (days > settings.AmberAfterDays)
        {
            return new CheckOutcome(AWhile, values, Detail: detail);
        }

        return new CheckOutcome(waiting > 0 ? WorkingWithWaiting : Working, values, Detail: detail);
    }
}
