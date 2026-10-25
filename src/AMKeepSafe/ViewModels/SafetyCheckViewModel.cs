using System.Collections.ObjectModel;
using System.Text;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMKeepSafe.ViewModels;

/// <summary>The Safety check tab. Reads only: nothing on this tab changes the PC.</summary>
public sealed partial class SafetyCheckViewModel(
    SafetyCheckRunner runner,
    CheckCatalog catalog,
    IShellLauncher shell,
    IDialogService dialogs,
    IAppLog log,
    FixCoordinator? fixes = null,
    ResultLinks? links = null) : ObservableObject
{
    private IReadOnlyList<CheckResult> _latest = [];

    public ObservableCollection<CheckResultViewModel> Results { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckButtonText))]
    private bool _hasResults;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private int _checksDone;

    [ObservableProperty]
    private int _checksTotal = 1;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private string _headline = "";

    public string CheckButtonText => HasResults ? "Check again" : "Check my PC";

    public string WhatWeCheckedLink => catalog.Summary.WhatWeCheckedLink;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task CheckAsync()
    {
        log.Info("User started the safety check.");
        IsChecking = true;
        ChecksDone = 0;
        ProgressText = "Checking your PC. This usually takes about a minute.";
        try
        {
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                ChecksTotal = Math.Max(p.Total, 1);
                ChecksDone = p.Done;
                ProgressText = $"Checking your PC. {p.Done} of {p.Total} checks done.";
            });

            _latest = await runner.RunAsync(progress).ConfigureAwait(true);

            Results.Clear();
            foreach (var result in SafetyCheckSummary.InDisplayOrder(_latest))
            {
                Results.Add(Wrap(result));
            }

            Headline = SafetyCheckSummary.Headline(_latest, catalog.Summary);
            HasResults = true;
        }
        finally
        {
            IsChecking = false;
        }
    }

    private CheckResultViewModel Wrap(CheckResult result) => new(
        result,
        catalog.StatusLabels[result.Status],
        shell,
        dialogs,
        log,
        fixes?.FixesFor(result.CheckId, result.OutcomeCode),
        fixes is null ? null : RunFixAsync,
        fixes?.Catalog.Common.WorkingText ?? "",
        links);

    /// <summary>Runs a fix, then checks that one thing again so the result shows what's true now.</summary>
    private async Task RunFixAsync(CheckResultViewModel item, FixDefinition fix)
    {
        item.IsBusy = true;
        try
        {
            if (!await fixes!.RunAsync(fix).ConfigureAwait(true))
            {
                return;
            }

            if (await runner.RunCheckAsync(item.CheckId).ConfigureAwait(true) is not { } fresh)
            {
                return;
            }

            _latest = _latest.Select(r => r.CheckId == fresh.CheckId ? fresh : r).ToList();
            var index = Results.IndexOf(item);
            if (index >= 0)
            {
                // Stays where it is, so nothing jumps around under the person's mouse.
                Results[index] = Wrap(fresh);
            }

            Headline = SafetyCheckSummary.Headline(_latest, catalog.Summary);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    [RelayCommand]
    private void ShowWhatWeChecked()
    {
        var text = new StringBuilder();
        foreach (var check in catalog.Checks)
        {
            text.Append(check.Title).Append(": ").AppendLine(check.WhatWeCheck).AppendLine();
        }

        text.Append("AM Keep Safe is not an antivirus. It checks how Windows is set up, and uses Microsoft Defender for virus scans.");
        dialogs.ShowMessage("What we checked", text.ToString());
    }
}
