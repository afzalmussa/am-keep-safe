namespace AMKeepSafe.Core.Checks;

/// <summary>
/// What a check found, before any wording is applied. <see cref="Code"/> picks the outcome in
/// checks.json, which decides the colour and the words.
/// </summary>
/// <param name="Code">The outcome's name in checks.json, for example "allOn".</param>
/// <param name="Values">Filled into {placeholders} in the wording, for example the date updates last installed.</param>
/// <param name="Items">A list to show under the result, for example the remote access tools found.</param>
/// <param name="Detail">What was actually read from Windows, for the log only.</param>
public sealed record CheckOutcome(
    string Code,
    IReadOnlyDictionary<string, string>? Values = null,
    IReadOnlyList<string>? Items = null,
    string? Detail = null)
{
    public IReadOnlyDictionary<string, string> Values { get; init; } = Values ?? new Dictionary<string, string>();

    public IReadOnlyList<string> Items { get; init; } = Items ?? [];
}

/// <summary>A finished check with its wording filled in, ready to show.</summary>
public sealed record CheckResult(
    string CheckId,
    string Title,
    string OutcomeCode,
    CheckStatus Status,
    string Summary,
    IReadOnlyList<string> HowToFix,
    IReadOnlyList<CheckAction> Actions,
    IReadOnlyList<string> Items);
