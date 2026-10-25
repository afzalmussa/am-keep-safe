namespace AMKeepSafe.Core.Checks;

/// <summary>One check on the Safety check tab. Reads only. Nothing here changes the PC.</summary>
public interface ISafetyCheck
{
    /// <summary>Matches the check's id in checks.json.</summary>
    string Id { get; }

    /// <summary>Every outcome code this check can return. The tests make sure each one has wording.</summary>
    IReadOnlyCollection<string> OutcomeCodes { get; }

    Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken);
}
