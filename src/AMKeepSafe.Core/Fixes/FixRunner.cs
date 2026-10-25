using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Undo;

namespace AMKeepSafe.Core.Fixes;

public enum FixOutcome
{
    /// <summary>Every change was made and checked.</summary>
    Done,

    /// <summary>Some changes were made, some weren't.</summary>
    PartlyDone,

    /// <summary>Nothing was changed because Windows refused or something went wrong.</summary>
    Failed,

    /// <summary>Test mode: nothing was changed, only logged.</summary>
    DryRun,
}

public sealed record FixRunResult(FixOutcome Outcome, IReadOnlyList<SettingChange> Made, IReadOnlyList<SettingChange> NotMade);

/// <summary>
/// Makes the changes in a confirmed preview, one at a time. For each change it:
/// refuses anything that would weaken protection (rule 6), records the old value before touching
/// anything (rule 5), makes the change, reads it back to be sure, and logs every step (rule 9).
/// In test mode it only logs what it would have done (rule 11).
/// </summary>
public sealed class FixRunner(ISystemChanger changer, IUndoJournal journal, IAppLog log, TimeProvider time, bool dryRun)
{
    public bool IsDryRun => dryRun;

    public async Task<FixRunResult> ApplyAsync(FixPreview preview, CancellationToken cancellationToken = default)
    {
        if (!preview.IsConfirmed)
        {
            // Trust rule 4: nothing changes without a preview the person has confirmed.
            throw new InvalidOperationException("A change was attempted without the person confirming the preview.");
        }

        if (dryRun)
        {
            foreach (var change in preview.Changes)
            {
                log.Info($"Would change: {change.Description}. {change.TechnicalDetail}.");
            }

            return new FixRunResult(FixOutcome.DryRun, [], []);
        }

        var made = new List<SettingChange>();
        var notMade = new List<SettingChange>();
        foreach (var change in preview.Changes)
        {
            if (await MakeAsync(change, record: true, cancellationToken).ConfigureAwait(false))
            {
                made.Add(change);
            }
            else
            {
                notMade.Add(change);
            }
        }

        var outcome = notMade.Count == 0 ? FixOutcome.Done : made.Count == 0 ? FixOutcome.Failed : FixOutcome.PartlyDone;
        return new FixRunResult(outcome, made, notMade);
    }

    /// <summary>Makes one change. Used by undo too, which records the undo itself instead of a new entry.</summary>
    internal async Task<bool> MakeAsync(SettingChange change, bool record, CancellationToken cancellationToken)
    {
        if (ProtectionGuard.WouldWeaken(change))
        {
            log.Error($"Refused a change that would weaken protection: {change.TechnicalDetail}.");
            return false;
        }

        UndoEntry? entry = null;
        if (record && change.Undo != UndoSupport.NotNeeded)
        {
            entry = new UndoEntry
            {
                Id = Guid.NewGuid(),
                When = time.GetUtcNow(),
                Kind = change.Kind switch
                {
                    ChangeKind.RegistryValue => UndoKind.RegistryValueChanged,
                    ChangeKind.ServiceStart => UndoKind.ServiceStartTypeChanged,
                    _ => UndoKind.SettingChanged,
                },
                DisplayName = change.Description,
                Data = change.ToUndoData(),
                CanUndoInApp = change.Undo == UndoSupport.InApp,
            };

            try
            {
                journal.Add(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Trust rule 5: no way back, no change.
                log.Error($"Couldn't record the old value, so this wasn't changed: {change.TechnicalDetail}.", ex);
                return false;
            }
        }

        log.Info($"Changing: {change.Description}. {change.TechnicalDetail}.");
        try
        {
            await changer.ApplyAsync(change, cancellationToken).ConfigureAwait(false);

            if (change.Kind != ChangeKind.Task)
            {
                var now = await changer.ReadAsync(change, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(now, change.To, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Windows reports \"{now ?? "not set"}\" after the change, not \"{change.To ?? "not set"}\".");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error($"The change didn't go through: {change.TechnicalDetail}.", ex);
            if (entry is not null)
            {
                journal.MarkNotMade(entry.Id);
            }

            return false;
        }

        log.Info($"Done: {change.Description}.");
        return true;
    }
}
