using AMKeepSafe.Core.Configuration;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMKeepSafe.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IAppLog _log;
    private readonly IShellLauncher _shell;
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;

    public MainViewModel(IAppLog log, IShellLauncher shell, IDialogService dialogs, AppSettings settings, AppOptions options)
    {
        _log = log;
        _shell = shell;
        _dialogs = dialogs;
        _settings = settings;
        IsDryRun = options.DryRun;
    }

    public string ProductName => AppInfo.ProductName;

    public string VersionText => $"Version {AppInfo.Version}";

    /// <summary>True when started with --dry-run. Nothing on the PC is changed, only logged.</summary>
    public bool IsDryRun { get; }

    // Licensing arrives in phase 6. Until then everyone is on the free version.
    public string LicenceStatusText => "Free version";

    public bool IsPro => false;

    /// <summary>The header's "Get Pro" button shows on the free version when a checkout link is set up.</summary>
    public bool CanGetPro => !IsPro && _settings.HasCheckoutUrl;

    [RelayCommand]
    private void ViewLog()
    {
        _log.Info("User opened the log.");

        if (_shell.OpenFile(_log.CurrentLogFile) || _shell.OpenFolder(_log.LogDirectory))
        {
            return;
        }

        _dialogs.ShowMessage(
            "Couldn't open the log",
            $"The log couldn't be opened automatically. You can find it in this folder:\n\n{_log.LogDirectory}");
    }

    [RelayCommand]
    private void CheckForUpdates() => Open(AppLinks.Releases, "the updates page");

    [RelayCommand]
    private void ReportProblem() => Open(AppLinks.Issues, "the problem report page");

    [RelayCommand]
    private void GetHelp() => Open(AppLinks.Contact, "the help page");

    [RelayCommand]
    private void OpenWebsite() => Open(AppLinks.Website, "the AM Software website");

    [RelayCommand]
    private void EnterLicenceKey()
    {
        _log.Info("User clicked Enter licence key. Licence keys aren't supported in this build yet.");
        _dialogs.ShowMessage(
            "Licence keys aren't ready yet",
            "This test version can't take a licence key yet. Every safety feature in AM Keep Safe is free, with or without a key.");
    }

    [RelayCommand]
    private void GetPro()
    {
        if (_settings.HasCheckoutUrl)
        {
            _log.Info("Opening the AM Keep Safe Pro checkout page.");
            Open(_settings.CheckoutUrl!, "the AM Keep Safe Pro page");
        }
    }

    private void Open(string url, string description)
    {
        if (!_shell.OpenUrl(url))
        {
            _dialogs.ShowMessage(
                "Couldn't open your web browser",
                $"We tried to open {description} but your web browser didn't start. You can type this address into your browser instead:\n\n{url}");
        }
    }
}
