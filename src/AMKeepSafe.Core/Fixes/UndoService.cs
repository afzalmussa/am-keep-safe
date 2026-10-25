using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Undo;

namespace AMKeepSafe.Core.Fixes;

public enum UndoOutcome
{
    Done,
    Failed,
    DryRun,

    /// <summary>This change is undone in Windows Settings, not by the app (trust rule 6).</summary>
    NotInApp,
}

/// <summary>
/// Puts back changes from the undo list. A change that switched a protection on is never switched
/// off by the app: the person is pointed to Windows Settings instead (trust rule 6).
/// </summary>
public sealed class UndoService(FixRunner runner, ISystemChanger changer, IUndoJournal journal, FixCommonText text, IAppLog log, TimeProvider time)
{
    /// <summary>Changes the person can see on the undo list, newest first. Changes that never happened are left out.</summary>
    public IReadOnlyList<UndoEntry> Entries() => journal.GetAll().Where(e => !e.NotMade).OrderByDescending(e => e.When).ToList();

    /// <summary>The preview for undoing an entry, or null if it can't be undone in the app.</summary>
    public async Task<FixPreview?> PreviewAsync(UndoEntry entry, CancellationToken cancellationToken = default)
    {
        if (!entry.CanUndoInApp || entry.IsUndone || SettingChange.FromUndoData(entry.Data) is not { } original)
        {
            return null;
        }

        var now = await changer.ReadAsync(original, cancellationToken).ConfigureAwait(false);
        var reversed = original.Reversed() with { From = now };
        var changedSince = !string.Equals(now, original.To, StringComparison.OrdinalIgnoreCase);
        return FixPreview.ForUndo(text, reversed, runner.IsDryRun, changedSince);
    }

    public async Task<UndoOutcome> UndoAsync(UndoEntry entry, FixPreview preview, CancellationToken cancellationToken = default)
    {
        if (!preview.IsConfirmed)
        {
            throw new InvalidOperationException("An undo was attempted without the person confirming the preview.");
        }

        if (!entry.CanUndoInApp || preview.Changes.Count != 1)
        {
            log.Warning($"Undo isn't done in the app for: {entry.DisplayName}.");
            return UndoOutcome.NotInApp;
        }

        var change = preview.Changes[0];
        log.Info($"User asked to undo: {entry.DisplayName}.");
        if (runner.IsDryRun)
        {
            log.Info($"Would undo: {change.Description}. {change.TechnicalDetail}.");
            return UndoOutcome.DryRun;
        }

        if (!await runner.MakeAsync(change, record: false, cancellationToken).ConfigureAwait(false))
        {
            return UndoOutcome.Failed;
        }

        journal.MarkUndone(entry.Id, time.GetUtcNow());
        log.Info($"Undone: {entry.DisplayName}.");
        return UndoOutcome.Done;
    }
}
