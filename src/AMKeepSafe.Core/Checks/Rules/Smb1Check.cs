namespace AMKeepSafe.Core.Checks.Rules;

public enum FeatureState
{
    Enabled,
    Disabled,
    NotPresent,
    Unknown,
}

/// <param name="Feature">The SMB1 optional Windows feature.</param>
/// <param name="ServerAcceptsSmb1">Whether file sharing on this PC still answers SMB1. Null if it couldn't be told.</param>
public sealed record Smb1Facts(FeatureState Feature, bool? ServerAcceptsSmb1);

public interface ISmb1Info
{
    Task<Smb1Facts> GetAsync(CancellationToken cancellationToken);
}

/// <summary>SMB1, a very old way of sharing files between computers with well-known weaknesses.</summary>
public sealed class Smb1Check(ISmb1Info info) : ISafetyCheck
{
    public const string CheckId = "smb1";

    public const string Off = "off";
    public const string On = "on";

    public string Id => CheckId;

    public IReadOnlyCollection<string> OutcomeCodes => [Off, On];

    public async Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) =>
        Evaluate(await info.GetAsync(cancellationToken).ConfigureAwait(false));

    public static CheckOutcome Evaluate(Smb1Facts facts)
    {
        var detail = $"SMB1 feature: {facts.Feature}. Server accepts SMB1: {facts.ServerAcceptsSmb1?.ToString() ?? "unknown"}.";
        // When the feature itself is off or removed, SMB1 can't work whatever the server setting says.
        if (facts.Feature == FeatureState.Enabled || (facts.Feature == FeatureState.Unknown && facts.ServerAcceptsSmb1 == true))
        {
            return new CheckOutcome(On, Detail: detail);
        }

        if (facts.Feature == FeatureState.Unknown && facts.ServerAcceptsSmb1 is null)
        {
            return new CheckOutcome(CheckCatalog.CouldNotCheckCode, Detail: detail);
        }

        return new CheckOutcome(Off, Detail: detail);
    }
}
