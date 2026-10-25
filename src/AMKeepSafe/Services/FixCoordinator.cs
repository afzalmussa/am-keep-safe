using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Logging;

namespace AMKeepSafe.Services;

/// <summary>
/// Takes a fix from button to finish: plan the changes, show the preview, make the changes only if
/// the person confirms, then say calmly what happened. Every step is logged.
/// </summary>
public sealed class FixCoordinator(IReadOnlyList<IFix> fixes, FixCatalog catalog, FixRunner runner, IDialogService dialogs, IAppLog log)
{
    public FixCatalog Catalog => catalog;

    /// <summary>The fixes to offer under a result, in the order fixes.json lists them.</summary>
    public IReadOnlyList<FixDefinition> FixesFor(string checkId, string outcomeCode) =>
        catalog.For(checkId, outcomeCode).Where(d => fixes.Any(f => f.Id == d.Id)).ToList();

    /// <returns>True if anything may have changed, so the result should be checked again.</returns>
    public async Task<bool> RunAsync(FixDefinition definition, CancellationToken cancellationToken = default)
    {
        log.Info($"User clicked \"{definition.ButtonLabel}\" ({definition.Id}).");
        var fix = fixes.Single(f => f.Id == definition.Id);

        IReadOnlyList<SettingChange> changes;
        try
        {
            changes = await fix.PlanAsync(definition, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error($"Couldn't work out what the fix '{definition.Id}' needs to change.", ex);
            dialogs.ShowMessage(catalog.Common.FailedTitle, definition.FailedText);
            return false;
        }

        if (changes.Count == 0)
        {
            log.Info($"Fix '{definition.Id}': nothing needs changing.");
            dialogs.ShowMessage(catalog.Common.NothingToDoTitle, catalog.Common.NothingToDo);
            return true;
        }

        var preview = FixPreview.ForFix(catalog.Common, definition, changes, runner.IsDryRun);
        log.Info($"Showing the preview for '{definition.Id}': {string.Join("; ", changes.Select(c => c.TechnicalDetail))}.");
        if (!dialogs.ConfirmPreview(preview))
        {
            log.Info($"User cancelled the fix '{definition.Id}'. Nothing was changed.");
            return false;
        }

        log.Info($"User confirmed the fix '{definition.Id}'.");
        var result = await runner.ApplyAsync(preview, cancellationToken).ConfigureAwait(true);

        var common = catalog.Common;
        var (title, message) = result.Outcome switch
        {
            FixOutcome.Done => (common.DoneTitle, preview.RestartNote is null ? definition.DoneText : $"{definition.DoneText} {preview.RestartNote}"),
            FixOutcome.PartlyDone => (common.PartlyDoneTitle, common.PartlyDoneText),
            FixOutcome.DryRun => (common.DryRunTitle, common.DryRunDone),
            _ => (common.FailedTitle, definition.FailedText),
        };
        log.Info($"Fix '{definition.Id}' finished: {result.Outcome}.");
        dialogs.ShowMessage(title, message);
        return result.Outcome is FixOutcome.Done or FixOutcome.PartlyDone;
    }
}
