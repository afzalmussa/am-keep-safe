namespace AMKeepSafe.Core.Fixes;

/// <summary>One line in the preview.</summary>
/// <param name="Description">What will change, in everyday words.</param>
/// <param name="TechnicalDetail">Exactly what changes, for anyone who wants to know.</param>
/// <param name="UndoNote">How it can be undone, or why it can't.</param>
public sealed record PreviewItem(string Description, string TechnicalDetail, string UndoNote);

/// <summary>
/// What the person sees before anything changes (trust rule 4). The runner only applies a preview
/// that has been confirmed, and it applies exactly the changes listed in it, nothing more.
/// </summary>
public sealed class FixPreview
{
    private FixPreview(string title, string intro, IReadOnlyList<SettingChange> changes, IReadOnlyList<PreviewItem> items, string? restartNote, string confirmButton, string cancelButton, string detailsLabel, bool isDryRun)
    {
        Title = title;
        Intro = intro;
        Changes = changes;
        Items = items;
        RestartNote = restartNote;
        ConfirmButton = confirmButton;
        CancelButton = cancelButton;
        DetailsLabel = detailsLabel;
        IsDryRun = isDryRun;
    }

    public string Title { get; }

    public string Intro { get; }

    public IReadOnlyList<SettingChange> Changes { get; }

    public IReadOnlyList<PreviewItem> Items { get; }

    /// <summary>Set when any change needs a restart to finish.</summary>
    public string? RestartNote { get; }

    public string ConfirmButton { get; }

    public string CancelButton { get; }

    public string DetailsLabel { get; }

    public bool IsDryRun { get; }

    /// <summary>Only set by <see cref="Confirm"/>, which the screen calls when the person clicks the confirm button.</summary>
    public bool IsConfirmed { get; private set; }

    public void Confirm() => IsConfirmed = true;

    public static FixPreview ForFix(FixCommonText text, FixDefinition fix, IReadOnlyList<SettingChange> changes, bool isDryRun) =>
        Build(text, fix.Title, changes, isDryRun, text.ConfirmButton, isUndo: false);

    public static FixPreview ForUndo(FixCommonText text, SettingChange reversed, bool isDryRun, bool changedSince) =>
        Build(text, text.UndoTitle, [reversed], isDryRun, text.UndoConfirmButton, isUndo: true, changedSince);

    private static FixPreview Build(FixCommonText text, string title, IReadOnlyList<SettingChange> changes, bool isDryRun, string confirm, bool isUndo, bool changedSince = false)
    {
        var items = changes.Select(c => new PreviewItem(c.Description, c.TechnicalDetail, isUndo ? (changedSince ? text.ChangedSinceNote : "") : UndoNote(text, c.Undo))).ToList();
        var intro = isDryRun ? text.DryRunIntro : isUndo ? text.UndoIntro : text.PreviewIntro;
        return new FixPreview(
            title,
            intro,
            changes,
            items,
            changes.Any(c => c.NeedsRestart) ? text.RestartNeeded : null,
            isDryRun ? text.DryRunConfirmButton : confirm,
            text.CancelButton,
            text.DetailsLabel,
            isDryRun);
    }

    public static string UndoNote(FixCommonText text, UndoSupport undo) => undo switch
    {
        UndoSupport.InApp => text.UndoInApp,
        UndoSupport.ViaSettings => text.UndoViaSettings,
        _ => text.UndoNotNeeded,
    };
}
