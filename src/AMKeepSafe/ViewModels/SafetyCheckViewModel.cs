using System.Collections.ObjectModel;
using System.Text;
using AMKeepSafe.Core.Checks;
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
    IAppLog log) : ObservableObject
{
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

            var results = await runner.RunAsync(progress).ConfigureAwait(true);

            Results.Clear();
            foreach (var result in SafetyCheckSummary.InDisplayOrder(results))
            {
                Results.Add(new CheckResultViewModel(result, catalog.StatusLabels[result.Status], shell, dialogs, log));
            }

            Headline = SafetyCheckSummary.Headline(results, catalog.Summary);
            HasResults = true;
        }
        finally
        {
            IsChecking = false;
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
