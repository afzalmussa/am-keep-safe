namespace AMKeepSafe.Core.Checks.Rules;

public enum EncryptionState
{
    NotEncrypted,
    Encrypted,
    Encrypting,
    Decrypting,
    Unknown,
}

/// <param name="Available">False when this PC or edition of Windows doesn't offer drive encryption.</param>
/// <param name="ProtectionOn">The drive is encrypted and locked to this PC. False while encryption is suspended.</param>
public sealed record EncryptionFacts(bool Available, EncryptionState State, bool ProtectionOn);

public interface IEncryptionInfo
{
    /// <summary>The state of the drive Windows is on.</summary>
    Task<EncryptionFacts> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Device encryption or BitLocker on the Windows drive. Information rather than pass or fail.
/// When it's on, what matters most is that the person knows where their recovery key is.
/// </summary>
public sealed class EncryptionCheck(IEncryptionInfo info) : ISafetyCheck
{
    public const string CheckId = "encryption";

    public const string On = "on";
    public const string Suspended = "suspended";
    public const string TurningOn = "turningOn";
    public const string Off = "off";
    public const string NotAvailable = "notAvailable";

    public string Id => CheckId;

    public IReadOnlyCollection<string> OutcomeCodes => [On, Suspended, TurningOn, Off, NotAvailable];

    public async Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) =>
        Evaluate(await info.GetAsync(cancellationToken).ConfigureAwait(false));

    public static CheckOutcome Evaluate(EncryptionFacts facts)
    {
        var detail = $"Available: {facts.Available}. State: {facts.State}. Protection on: {facts.ProtectionOn}.";
        if (!facts.Available)
        {
            return new CheckOutcome(NotAvailable, Detail: detail);
        }

        var code = facts.State switch
        {
            EncryptionState.Encrypted => facts.ProtectionOn ? On : Suspended,
            EncryptionState.Encrypting => TurningOn,
            EncryptionState.NotEncrypted or EncryptionState.Decrypting => Off,
            _ => CheckCatalog.CouldNotCheckCode,
        };
        return new CheckOutcome(code, Detail: detail);
    }
}
