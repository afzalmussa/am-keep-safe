using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMKeepSafe.ViewModels;

/// <summary>
/// One result on the Safety check. Deliberately has nothing to do with Pro: a red or amber
/// result only ever offers free help (trust rule 2).
/// </summary>
public sealed partial class CheckResultViewModel : ObservableObject
{
    public CheckResultViewModel(
        CheckResult result,
        string statusLabel,
        IShellLauncher shell,
        IDialogService dialogs,
        IAppLog log,
        IReadOnlyList<FixDefinition>? fixes = null,
        Func<CheckResultViewModel, FixDefinition, Task>? runFix = null,
        string workingText = "",
        ResultLinks? links = null)
    {
        Result = result;
        StatusLabel = statusLabel;
        WorkingText = workingText;
        Steps = result.HowToFix.Select((text, i) => new StepViewModel(i + 1, text)).ToList();
        Actions = result.Actions.Select(a => new ResultActionViewModel(a, result.Title, shell, dialogs, log, links)).ToList();
        Fixes = runFix is null ? [] : (fixes ?? []).Select(f => new FixButtonViewModel(f, () => runFix(this, f), () => !IsBusy)).ToList();
    }

    public CheckResult Result { get; }

    public string CheckId => Result.CheckId;

    public string Title => Result.Title;

    public CheckStatus Status => Result.Status;

    public string StatusLabel { get; }

    public string Summary => Result.Summary;

    public IReadOnlyList<string> Items => Result.Items;

    public bool HasItems => Items.Count > 0;

    public IReadOnlyList<StepViewModel> Steps { get; }

    public bool HasSteps => Steps.Count > 0;

    public IReadOnlyList<ResultActionViewModel> Actions { get; }

    /// <summary>Free fixes for this result, such as "Fix this" or "Run a quick scan". Never a paid upgrade (trust rule 2).</summary>
    public IReadOnlyList<FixButtonViewModel> Fixes { get; }

    public bool HasFixes => Fixes.Count > 0;

    /// <summary>Shown while a fix or scan is running.</summary>
    public string WorkingText { get; }

    [ObservableProperty]
    private bool _isBusy;

    partial void OnIsBusyChanged(bool value)
    {
        foreach (var fix in Fixes)
        {
            fix.RunCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Everything a screen reader needs in one go.</summary>
    public string AccessibleName => $"{Title}. {StatusLabel}. {Summary}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepsButtonText))]
    private bool _stepsOpen;

    /// <summary>"How to fix" for red and amber, "What you can do" for everything else.</summary>
    public string StepsButtonText => StepsOpen ? "Hide the steps" : Status.NeedsAction() ? "How to fix" : "What you can do";

    [RelayCommand]
    private void ToggleSteps() => StepsOpen = !StepsOpen;
}

/// <summary>A button that starts a fix. The fix itself always shows a preview first.</summary>
public sealed partial class FixButtonViewModel(FixDefinition definition, Func<Task> run, Func<bool> canRun)
{
    public string Label => definition.ButtonLabel;

    public FixDefinition Definition => definition;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunAsync() => run();

    private bool CanRun() => canRun();
}

public sealed record StepViewModel(int Number, string Text)
{
    public string NumberText => $"{Number}.";
}

/// <summary>What result buttons need from the rest of the app: browser page addresses, and moving between tabs.</summary>
public sealed record ResultLinks(Func<Browser, BrowserPage, string?> BrowserPageAddress, Action<AppTab> GoToTab);

/// <summary>A button under a result that opens a Windows settings page, a web page, a browser page or another tab.</summary>
public sealed partial class ResultActionViewModel(CheckAction action, string checkTitle, IShellLauncher shell, IDialogService dialogs, IAppLog log, ResultLinks? links = null)
{
    public string Label => action.Label;

    public CheckAction Action => action;

    [RelayCommand]
    private void Open()
    {
        log.Info($"User clicked \"{action.Label}\" on the {checkTitle} result.");
        if (action.SettingsPage is { } page)
        {
            if (!shell.OpenSettings(page))
            {
                dialogs.ShowMessage(
                    "Couldn't open that page",
                    "Windows didn't open the settings page. You can find it by opening the Start menu, clicking Settings, and typing what you're looking for into the search box.");
            }
        }
        else if (action.Url is { } url && !shell.OpenUrl(url))
        {
            dialogs.ShowMessage(
                "Couldn't open your web browser",
                $"Your web browser didn't start. You can type this address into your browser instead:\n\n{url}");
        }
        else if (action.BrowserPage is { } browserPage && action.Browser is { } browser)
        {
            var address = links?.BrowserPageAddress(browser, browserPage);
            if (address is null || !shell.OpenBrowserPage(browser, address))
            {
                dialogs.ShowMessage(
                    "Couldn't open the browser",
                    address is null
                        ? "The browser didn't open. You can open it yourself and find this in its settings."
                        : $"The browser didn't open. You can open it yourself and type this into its address bar:\n\n{address}");
            }
        }
        else if (action.AppTab is { } tab)
        {
            links?.GoToTab(tab);
        }
    }
}
