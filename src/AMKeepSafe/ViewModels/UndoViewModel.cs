using System.Collections.ObjectModel;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Core.Text;
using AMKeepSafe.Core.Undo;
using AMKeepSafe.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMKeepSafe.ViewModels;

/// <summary>The "Undo changes" window: everything the app has changed, and a way back for each (trust rule 5).</summary>
public sealed partial class UndoViewModel(UndoService undo, FixCommonText text, IDialogService dialogs, IShellLauncher shell, IAppLog log) : ObservableObject
{
    public FixCommonText Text => text;

    public ObservableCollection<UndoEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    private bool _isEmpty = true;

    public void Refresh()
    {
        Entries.Clear();
        foreach (var entry in undo.Entries())
        {
            Entries.Add(new UndoEntryViewModel(entry, text, UndoAsync, OpenSettings));
        }

        IsEmpty = Entries.Count == 0;
    }

    private async Task UndoAsync(UndoEntry entry)
    {
        var preview = await undo.PreviewAsync(entry).ConfigureAwait(true);
        if (preview is null)
        {
            dialogs.ShowMessage(text.UndoTitle, text.ViaSettingsNote);
            return;
        }

        if (!dialogs.ConfirmPreview(preview))
        {
            log.Info($"User cancelled undoing: {entry.DisplayName}.");
            return;
        }

        var outcome = await undo.UndoAsync(entry, preview).ConfigureAwait(true);
        var (title, message) = outcome switch
        {
            UndoOutcome.Done => (text.DoneTitle, preview.RestartNote is null ? text.UndoDone : $"{text.UndoDone} {preview.RestartNote}"),
            UndoOutcome.DryRun => (text.DryRunTitle, text.DryRunDone),
            UndoOutcome.NotInApp => (text.UndoTitle, text.ViaSettingsNote),
            _ => (text.FailedTitle, text.UndoFailed),
        };
        dialogs.ShowMessage(title, message);
        Refresh();
    }

    private void OpenSettings(WindowsSettingsPage page)
    {
        log.Info($"User opened Windows Settings ({page}) from the undo list.");
        if (!shell.OpenSettings(page))
        {
            dialogs.ShowMessage(
                "Couldn't open that page",
                "Windows didn't open the settings page. You can find it by opening the Start menu, clicking Settings, and typing what you're looking for into the search box.");
        }
    }
}

public sealed partial class UndoEntryViewModel
{
    private readonly UndoEntry _entry;
    private readonly Func<UndoEntry, Task> _undo;
    private readonly Action<WindowsSettingsPage> _openSettings;
    private readonly WindowsSettingsPage? _page;

    public UndoEntryViewModel(UndoEntry entry, FixCommonText text, Func<UndoEntry, Task> undo, Action<WindowsSettingsPage> openSettings)
    {
        _entry = entry;
        _undo = undo;
        _openSettings = openSettings;
        _page = SettingChange.FromUndoData(entry.Data)?.UndoPage;
        UndoButtonText = text.UndoButton;
        OpenSettingsButtonText = text.OpenSettingsButton;
        Note = entry.IsUndone
            ? CheckCatalog.Fill(text.UndoneOn, new Dictionary<string, string> { ["date"] = FriendlyText.DateAndTime(entry.UndoneAt!.Value) })
            : entry.CanUndoInApp ? "" : text.ViaSettingsNote;
    }

    public string DisplayName => _entry.DisplayName;

    public string WhenText => FriendlyText.DateAndTime(_entry.When);

    public string Note { get; }

    public bool HasNote => Note.Length > 0;

    public bool CanUndo => _entry.CanUndoInApp && !_entry.IsUndone;

    public bool CanOpenSettings => !_entry.CanUndoInApp && !_entry.IsUndone && _page is not null;

    public string UndoButtonText { get; }

    public string OpenSettingsButtonText { get; }

    [RelayCommand]
    private Task UndoAsync() => _undo(_entry);

    [RelayCommand]
    private void OpenSettings()
    {
        if (_page is { } page)
        {
            _openSettings(page);
        }
    }
}
