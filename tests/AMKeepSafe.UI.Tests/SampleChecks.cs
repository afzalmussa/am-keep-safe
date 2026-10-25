using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Configuration;
using AMKeepSafe.ViewModels;

namespace AMKeepSafe.UI.Tests;

/// <summary>Stand-in checks that return chosen outcomes, worded by the real checks.json.</summary>
internal static class SampleChecks
{
    public static readonly CheckCatalog Catalog = CheckCatalog.LoadEmbedded();

    private sealed class Stub(string id, CheckOutcome outcome) : ISafetyCheck
    {
        public string Id => id;

        public IReadOnlyCollection<string> OutcomeCodes => [outcome.Code];

        public Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) => Task.FromResult(outcome);
    }

    /// <summary>A PC with one of every colour: red, amber, information, couldn't check and green.</summary>
    public static IReadOnlyList<ISafetyCheck> Mixed() =>
    [
        new Stub(WindowsUpdateCheck.CheckId, new CheckOutcome(WindowsUpdateCheck.Working, new Dictionary<string, string> { ["lastInstalledAgo"] = "3 days ago", ["lastInstalledDate"] = "22 October 2026" })),
        new Stub(WindowsVersionCheck.CheckId, new CheckOutcome(WindowsVersionCheck.Win10NotGettingUpdates, new Dictionary<string, string> { ["windows10SupportEnded"] = "14 October 2025", ["esuEnds"] = "12 October 2027", ["version"] = "22H2" })),
        new Stub(AntivirusCheck.CheckId, new CheckOutcome(AntivirusCheck.DefenderOn)),
        new Stub(FirewallCheck.CheckId, new CheckOutcome(FirewallCheck.SomeOff, new Dictionary<string, string> { ["networks"] = "public networks, like in cafés" })),
        new Stub(UacCheck.CheckId, new CheckOutcome(UacCheck.On)),
        new Stub(EncryptionCheck.CheckId, new CheckOutcome(EncryptionCheck.On)),
        new Stub(BackupCheck.CheckId, new CheckOutcome(BackupCheck.NoFileBackup)),
        new Stub(RemoteDesktopCheck.CheckId, new CheckOutcome(RemoteDesktopCheck.Off)),
        new Stub(Smb1Check.CheckId, new CheckOutcome(CheckCatalog.CouldNotCheckCode)),
        new Stub(RemoteToolsCheck.CheckId, new CheckOutcome(RemoteToolsCheck.Found, Items: ["AnyDesk: installed", "TeamViewer: open right now"])),
        new Stub(SecureBootTpmCheck.CheckId, new CheckOutcome(SecureBootTpmCheck.BothOn)),
    ];

    public static IReadOnlyList<ISafetyCheck> Healthy() =>
        Catalog.Checks.Select(c => (ISafetyCheck)new Stub(c.Id, new CheckOutcome(c.Outcomes.First(o => o.Value.Status is CheckStatus.Good or CheckStatus.Info).Key))).ToList();

    public static SafetyCheckViewModel ViewModel(IReadOnlyList<ISafetyCheck> checks, FakeShell? shell = null, FakeDialogs? dialogs = null, FakeLog? log = null)
    {
        log ??= new FakeLog();
        return new SafetyCheckViewModel(new SafetyCheckRunner(checks, Catalog, log), Catalog, shell ?? new FakeShell(), dialogs ?? new FakeDialogs(), log);
    }

    public static MainViewModel MainWindowModel(SafetyCheckViewModel safetyCheck, bool dryRun = false) =>
        new(new FakeLog(), new FakeShell(), new FakeDialogs(), AppSettings.LoadEmbedded(),
            dryRun ? AppOptions.Parse(["--dry-run"]) : AppOptions.Default, safetyCheck);
}
