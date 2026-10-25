using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Services;
using AMKeepSafe.ViewModels;

namespace AMKeepSafe.UI.Tests;

/// <summary>
/// A small, real fix setup: the real fixes, runner, undo and wording, over settings held in memory.
/// The firewall and Remote Desktop checks read the same settings, so fixing them really changes the result.
/// </summary>
internal sealed class FixFlow
{
    public const string TerminalServer = @"SYSTEM\CurrentControlSet\Control\Terminal Server";

    public FixFlow(bool dryRun = false)
    {
        Changer.Set(ChangeKind.FirewallProfile, "Domain", "on").Set(ChangeKind.FirewallProfile, "Private", "on").Set(ChangeKind.FirewallProfile, "Public", "off")
            .Set(ChangeKind.RegistryValue, "fDenyTSConnections", "0", AMKeepSafe.Core.Platform.RegistryRoot.LocalMachine, TerminalServer);

        Runner = new FixRunner(Changer, Journal, Log, TimeProvider.System, dryRun);
        Coordinator = new FixCoordinator(FixSet.Create(Changer, "C:"), FixCatalog, Runner, Dialogs, Log);
        Undo = new UndoViewModel(new UndoService(Runner, Changer, Journal, FixCatalog.Common, Log, TimeProvider.System), FixCatalog.Common, Dialogs, Shell, Log);
    }

    public FakeChanger Changer { get; } = new();

    public MemoryJournal Journal { get; } = new();

    public FakeDialogs Dialogs { get; } = new();

    public FakeShell Shell { get; } = new();

    public FakeLog Log { get; } = new();

    public FixCatalog FixCatalog { get; } = FixCatalog.LoadEmbedded();

    public FixRunner Runner { get; }

    public FixCoordinator Coordinator { get; }

    public UndoViewModel Undo { get; }

    public SafetyCheckViewModel SafetyCheck() =>
        new(new SafetyCheckRunner([new FirewallStub(Changer), new RemoteDesktopStub(Changer), new AntivirusStub()], SampleChecks.Catalog, Log),
            SampleChecks.Catalog, Shell, Dialogs, Log, Coordinator);

    private sealed class FirewallStub(FakeChanger changer) : ISafetyCheck
    {
        public string Id => FirewallCheck.CheckId;

        public IReadOnlyCollection<string> OutcomeCodes => [FirewallCheck.AllOn, FirewallCheck.SomeOff];

        public Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) =>
            Task.FromResult(changer.Get(ChangeKind.FirewallProfile, "Public") == "on"
                ? new CheckOutcome(FirewallCheck.AllOn)
                : new CheckOutcome(FirewallCheck.SomeOff, new Dictionary<string, string> { ["networks"] = "public networks, like in cafés" }));
    }

    private sealed class RemoteDesktopStub(FakeChanger changer) : ISafetyCheck
    {
        public string Id => RemoteDesktopCheck.CheckId;

        public IReadOnlyCollection<string> OutcomeCodes => [RemoteDesktopCheck.On, RemoteDesktopCheck.Off];

        public Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) =>
            Task.FromResult(new CheckOutcome(changer.Get(ChangeKind.RegistryValue, "fDenyTSConnections", AMKeepSafe.Core.Platform.RegistryRoot.LocalMachine, TerminalServer) == "0"
                ? RemoteDesktopCheck.On : RemoteDesktopCheck.Off));
    }

    private sealed class AntivirusStub : ISafetyCheck
    {
        public string Id => AntivirusCheck.CheckId;

        public IReadOnlyCollection<string> OutcomeCodes => [AntivirusCheck.DefenderOn];

        public Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) =>
            Task.FromResult(new CheckOutcome(AntivirusCheck.DefenderOn));
    }
}
