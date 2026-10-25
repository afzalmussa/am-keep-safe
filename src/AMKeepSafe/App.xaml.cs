using System.Windows;
using System.Windows.Threading;
using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Configuration;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Core.Undo;
using AMKeepSafe.Services;
using AMKeepSafe.ViewModels;
using AMKeepSafe.Views;
using AMKeepSafe.Windows.Browsers;
using AMKeepSafe.Windows.Checks;
using AMKeepSafe.Windows.Fixes;
using AMKeepSafe.Windows.Platform;

namespace AMKeepSafe;

public partial class App : Application
{
    // Two copies running at once could try to change the same settings, so only one is allowed.
    private const string SingleInstanceName = @"Global\AMSoftware.KeepSafe.SingleInstance";

    private Mutex? _singleInstance;
    private IAppLog? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = AppOptions.Parse(e.Args);
        _log = new FileAppLog(FileAppLog.DefaultDirectory, options.DryRun);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            _log.Info("A second copy was started while AM Keep Safe was already open. Closing the second copy.");
            MessageWindow.Show(
                null,
                "AM Keep Safe is already open",
                "AM Keep Safe is already running. Look for it on the taskbar at the bottom of your screen.");
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        var systemInfo = new WindowsSystemInfo();
        _log.Info($"{AppInfo.ProductName} {AppInfo.Version} started on {systemInfo.WindowsDescription}. " +
                  $"Administrator: {(systemInfo.IsRunningAsAdministrator ? "yes" : "no")}. " +
                  $"Dry run: {(options.DryRun ? "yes, no changes will be made" : "no")}.");
        if (options.UnknownArguments.Count > 0)
        {
            _log.Warning($"Ignored unrecognised command-line arguments: {string.Join(' ', options.UnknownArguments)}");
        }

        var settings = AppSettings.LoadEmbedded();
        var shell = new WindowsShellLauncher(_log);
        var dialogs = new DialogService();

        var powerShell = new PowerShellRunner(_log);
        var registry = new WindowsRegistryReader();

        // The Browser check reads the browsers of the account the app runs as. Profile files are only read.
        var browserLocations = BrowserLocations.ForCurrentUser();
        var browserPolicies = new BrowserPolicyReader(registry, browserLocations);
        var browserText = BrowserText.LoadEmbedded();
        var browsers = new BrowserCheckRunner(
            new ChromiumReader(browserLocations),
            new FirefoxReader(browserLocations),
            browserPolicies,
            new PcContextInfo(powerShell),
            SearchEngineList.LoadEmbedded(),
            KnownBadExtensionList.LoadEmbedded(),
            browserText,
            _log);

        var checks = SafetyCheckSet.Create(new CheckSources(
            registry,
            new WindowsProcessList(),
            new WindowsUpdateInfo(powerShell),
            new AntivirusInfo(powerShell),
            new FirewallInfo(powerShell),
            new EncryptionInfo(powerShell),
            new SecureBootTpmInfo(powerShell),
            new Smb1Info(powerShell),
            RemoteToolList.LoadEmbedded(),
            TimeProvider.System,
            browsers));
        var catalog = CheckCatalog.LoadEmbedded();

        // Everything that changes the PC goes through the fix runner: preview, record, back up, change, check, log.
        var changer = new WindowsSystemChanger(powerShell, registry);
        var journal = new FileUndoJournal(AppPaths.UndoHistory, _log);
        var fixRunner = new FixRunner(changer, journal, _log, TimeProvider.System, options.DryRun, AppPaths.Backups);
        var fixCatalog = FixCatalog.LoadEmbedded();
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var fixes = new FixCoordinator(FixSet.Create(changer, systemDrive, browserPolicies), fixCatalog, fixRunner, dialogs, _log);
        var undo = new UndoViewModel(new UndoService(fixRunner, changer, journal, fixCatalog.Common, _log, TimeProvider.System), fixCatalog.Common, dialogs, shell, _log);

        // Result buttons can move between tabs, so they need the main view model, which is made last.
        MainViewModel? main = null;
        void GoToTab(AppTab tab) => main?.GoToTab(tab);

        var safetyCheck = new SafetyCheckViewModel(
            new SafetyCheckRunner(checks, catalog, _log), catalog, shell, dialogs, _log, fixes, new ResultLinks(browserText.PageAddress, GoToTab));
        var browserCheck = new BrowserCheckViewModel(browsers, shell, dialogs, _log, fixes, new WindowsBrowserProcesses(_log), GoToTab);

        var viewModel = main = new MainViewModel(_log, shell, dialogs, settings, options, safetyCheck, undo, browserCheck);

        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Info($"{AppInfo.ProductName} closed.");
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error("Something unexpected went wrong.", e.Exception);
        e.Handled = true;
        MessageWindow.Show(
            MainWindow is { IsVisible: true } ? MainWindow : null,
            "Something went wrong",
            "AM Keep Safe ran into a problem it didn't expect. " +
            "The details have been saved in the log, which you can open with \"View log\" at the bottom of the window.");
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _log?.Error("A serious error closed the app.", e.ExceptionObject as Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log?.Error("A background task failed.", e.Exception);
        e.SetObserved();
    }
}
