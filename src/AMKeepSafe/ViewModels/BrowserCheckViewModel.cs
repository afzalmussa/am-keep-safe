using System.Collections.ObjectModel;
using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMKeepSafe.ViewModels;

/// <summary>One browser profile on the Browser check tab.</summary>
public sealed class BrowserSectionViewModel(Browser browser, string label, IEnumerable<CheckResultViewModel> results)
{
    public Browser Browser => browser;

    public string Label => label;

    public ObservableCollection<CheckResultViewModel> Results { get; } = new(results);
}

/// <summary>
/// The Browser check tab. Reads only, apart from the free "Remove these settings" fix, which goes
/// through the same preview, backup and undo as every other fix. Shows what the Safety check found
/// too, so the person doesn't have to check twice.
/// </summary>
public sealed partial class BrowserCheckViewModel : ObservableObject
{
    private readonly BrowserCheckRunner _runner;
    private readonly IShellLauncher _shell;
    private readonly IDialogService _dialogs;
    private readonly IAppLog _log;
    private readonly FixCoordinator? _fixes;
    private readonly IBrowserProcesses? _processes;
    private readonly ResultLinks _links;
    private readonly SynchronizationContext? _ui;
    private BrowserCheckReport? _shown;

    public BrowserCheckViewModel(
        BrowserCheckRunner runner,
        IShellLauncher shell,
        IDialogService dialogs,
        IAppLog log,
        FixCoordinator? fixes = null,
        IBrowserProcesses? processes = null,
        Action<AppTab>? goToTab = null)
    {
        _runner = runner;
        _shell = shell;
        _dialogs = dialogs;
        _log = log;
        _fixes = fixes;
        _processes = processes;
        _links = new ResultLinks(runner.Text.PageAddress, goToTab ?? (_ => { }));
        _ui = SynchronizationContext.Current;

        // The Safety check runs the browser check too. Show its findings here as soon as they arrive.
        runner.Finished += report =>
        {
            if (_ui is null)
            {
                Show(report);
            }
            else
            {
                _ui.Post(_ => Show(report), null);
            }
        };
    }

    public BrowserText Text => _runner.Text;

    public ResultLinks Links => _links;

    public ObservableCollection<BrowserSectionViewModel> Sections { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckButtonText))]
    private bool _hasResults;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeadline))]
    private string _headline = "";

    public bool HasHeadline => Headline.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string? _note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public string CheckButtonText => HasResults ? Text.Notes.CheckAgainButton : Text.Notes.CheckButton;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task CheckAsync()
    {
        _log.Info("User started the browser check.");
        IsChecking = true;
        try
        {
            Show(await _runner.RunAsync().ConfigureAwait(true));
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void Show(BrowserCheckReport report)
    {
        // A check started here also raises Finished, so the same report can arrive twice.
        if (ReferenceEquals(report, _shown))
        {
            return;
        }

        _shown = report;
        Sections.Clear();
        foreach (var section in report.Sections)
        {
            Sections.Add(new BrowserSectionViewModel(section.Browser, section.Label, section.Results.Select(r => Wrap(r, section.Browser))));
        }

        var results = report.AllResults.ToList();
        Headline = report.NoBrowsers ? "" : SafetyCheckSummary.Headline(results, Text.Catalog.Summary);
        Note = report.Note;
        HasResults = true;
    }

    private CheckResultViewModel Wrap(CheckResult result, Browser browser) => new(
        result,
        Text.Catalog.StatusLabels[result.Status],
        _shell,
        _dialogs,
        _log,
        _fixes?.FixesFor(result.CheckId, result.OutcomeCode),
        _fixes is null ? null : (item, fix) => RunFixAsync(item, fix, browser),
        _fixes?.Catalog.Common.WorkingText ?? "",
        _links);

    /// <summary>Removes policies, offers to close the browser so it takes effect, then checks again.</summary>
    private async Task RunFixAsync(CheckResultViewModel item, FixDefinition fix, Browser browser)
    {
        item.IsBusy = true;
        try
        {
            if (!await _fixes!.RunAsync(fix).ConfigureAwait(true))
            {
                return;
            }

            // Policies only take effect when the browser restarts. Offer to close it, never just close it.
            if (_processes is null || _fixes.IsDryRun || !_processes.IsRunning(browser))
            {
                await CheckAsync().ConfigureAwait(true);
                return;
            }

            var values = new Dictionary<string, string> { ["browser"] = Text.NameOf(browser) };
            var close = _dialogs.Confirm(
                Text.Notes.CloseTitle,
                CheckCatalog.Fill(Text.Notes.CloseBody, values),
                CheckCatalog.Fill(Text.Notes.CloseYes, values),
                Text.Notes.CloseNo);
            _log.Info($"User chose {(close ? "to close" : "not to close")} {browser} after removing its policies.");

            if (close && !await _processes.CloseAsync(browser, TimeSpan.FromSeconds(15)).ConfigureAwait(true))
            {
                _dialogs.ShowMessage(Text.Notes.CloseTitle, CheckCatalog.Fill(Text.Notes.StillOpen, values));
            }

            await CheckAsync().ConfigureAwait(true);
        }
        finally
        {
            item.IsBusy = false;
        }
    }
}
