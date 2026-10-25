namespace AMKeepSafe.Core.Fixes;

/// <summary>
/// A fix for a check result. Reads the current state and plans only the changes that are needed,
/// with their wording from fixes.json. Planning never changes anything.
/// </summary>
public interface IFix
{
    /// <summary>Matches the fix's id in fixes.json.</summary>
    string Id { get; }

    /// <summary>Every change key this fix can plan. The tests make sure each one has wording.</summary>
    IReadOnlyCollection<string> ChangeKeys { get; }

    Task<IReadOnlyList<SettingChange>> PlanAsync(FixDefinition text, CancellationToken cancellationToken);
}
