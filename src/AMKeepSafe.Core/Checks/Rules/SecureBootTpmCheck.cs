namespace AMKeepSafe.Core.Checks.Rules;

public enum SecureBootState
{
    On,
    Off,
    NotSupported,
    Unknown,
}

/// <param name="TpmPresent">Null if it couldn't be told.</param>
/// <param name="TpmVersion">For example "2.0".</param>
public sealed record SecureBootTpmFacts(SecureBootState SecureBoot, bool? TpmPresent, bool? TpmReady, string? TpmVersion);

public interface ISecureBootTpmInfo
{
    Task<SecureBootTpmFacts> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Secure Boot and the TPM security chip. Shown as information only: most people can't change
/// these without going into the PC's start-up settings, and older PCs simply don't have them.
/// </summary>
public sealed class SecureBootTpmCheck(ISecureBootTpmInfo info) : ISafetyCheck
{
    public const string CheckId = "secure-boot-tpm";

    public const string BothOn = "bothOn";
    public const string SecureBootOff = "secureBootOff";
    public const string NoTpm = "noTpm";
    public const string NeitherOn = "neitherOn";

    public string Id => CheckId;

    public IReadOnlyCollection<string> OutcomeCodes => [BothOn, SecureBootOff, NoTpm, NeitherOn];

    public async Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) =>
        Evaluate(await info.GetAsync(cancellationToken).ConfigureAwait(false));

    public static CheckOutcome Evaluate(SecureBootTpmFacts facts)
    {
        var detail = $"Secure Boot: {facts.SecureBoot}. TPM present: {facts.TpmPresent?.ToString() ?? "unknown"}, ready: {facts.TpmReady?.ToString() ?? "unknown"}, version: {facts.TpmVersion ?? "unknown"}.";
        if (facts.SecureBoot == SecureBootState.Unknown && facts.TpmPresent is null)
        {
            return new CheckOutcome(CheckCatalog.CouldNotCheckCode, Detail: detail);
        }

        var secureBoot = facts.SecureBoot == SecureBootState.On;
        var tpm = facts.TpmPresent == true && facts.TpmReady != false;
        var values = new Dictionary<string, string> { ["tpmVersion"] = string.IsNullOrWhiteSpace(facts.TpmVersion) ? "" : facts.TpmVersion };
        var code = (secureBoot, tpm) switch
        {
            (true, true) => BothOn,
            (false, true) => SecureBootOff,
            (true, false) => NoTpm,
            _ => NeitherOn,
        };
        return new CheckOutcome(code, values, Detail: detail);
    }
}
