namespace AMKeepSafe.Core.Browsers;

/// <param name="IsWorkOrSchoolPc">Joined to a work or school network or device management, so browser policies are probably deliberate.</param>
/// <param name="RunningAs">The Windows account the app runs as, for example "PC\Pat".</param>
/// <param name="SignedInUser">The account signed in at the screen, if Windows says.</param>
public sealed record PcContext(bool IsWorkOrSchoolPc, string RunningAs, string? SignedInUser)
{
    public static PcContext Unknown { get; } = new(false, "", null);

    /// <summary>
    /// True when someone approved the administrator prompt with a different account, so "this
    /// account's" browsers are the administrator's, not the person at the screen's.
    /// </summary>
    public bool IsDifferentAccount =>
        !string.IsNullOrWhiteSpace(SignedInUser) && !string.IsNullOrWhiteSpace(RunningAs)
        && !string.Equals(SignedInUser.Trim(), RunningAs.Trim(), StringComparison.OrdinalIgnoreCase);
}

public interface IPcContextInfo
{
    Task<PcContext> GetAsync(CancellationToken cancellationToken);
}
