using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Checks.Rules;
using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Tests.Support;

/// <summary>A registry held in memory. Keys are matched ignoring capitals, like the real one.</summary>
public sealed class FakeRegistry : IRegistryReader
{
    private readonly Dictionary<string, Dictionary<string, object>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public FakeRegistry Set(RegistryRoot root, string key, string name, object value, bool use32BitView = false)
    {
        var path = Path(root, key, use32BitView);
        if (!_keys.TryGetValue(path, out var values))
        {
            _keys[path] = values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        values[name] = value;
        return this;
    }

    /// <summary>Adds an empty key, so it shows up as a sub-key of its parent.</summary>
    public FakeRegistry AddKey(RegistryRoot root, string key, bool use32BitView = false)
    {
        var path = Path(root, key, use32BitView);
        if (!_keys.ContainsKey(path))
        {
            _keys[path] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        return this;
    }

    public object? GetValue(RegistryRoot root, string key, string name, bool use32BitView = false) =>
        _keys.TryGetValue(Path(root, key, use32BitView), out var values) && values.TryGetValue(name, out var value) ? value : null;

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string key, bool use32BitView = false)
    {
        var prefix = Path(root, key, use32BitView) + "\\";
        return _keys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetValueNames(RegistryRoot root, string key, bool use32BitView = false) =>
        _keys.TryGetValue(Path(root, key, use32BitView), out var values) ? values.Keys.ToList() : [];

    private static string Path(RegistryRoot root, string key, bool use32BitView) => $"{root}{(use32BitView ? "32" : "")}\\{key}";
}

public sealed class FakeProcesses(params string[] names) : IProcessList
{
    public IReadOnlyCollection<string> RunningProgramNames() => names;
}

public sealed class FakeWindowsUpdate : IWindowsUpdateInfo
{
    public WindowsUpdateStatus Status { get; set; } = new(ServiceStart.Manual, DateTimeOffset.UnixEpoch, false, null);

    public int? Waiting { get; set; }

    public int WaitingCalls { get; private set; }

    public Task<WindowsUpdateStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Status);

    public Task<int?> CountWaitingUpdatesAsync(CancellationToken cancellationToken)
    {
        WaitingCalls++;
        return Task.FromResult(Waiting);
    }
}

/// <summary>Returns fixed facts for any of the PowerShell-based checks.</summary>
public sealed class FakeFacts<T>(T facts) :
    IAntivirusInfo, IFirewallInfo, IEncryptionInfo, ISecureBootTpmInfo, ISmb1Info
{
    public T Facts { get; set; } = facts;

    Task<AntivirusFacts> IAntivirusInfo.GetAsync(CancellationToken c) => Task.FromResult((AntivirusFacts)(object)Facts!);

    Task<FirewallFacts> IFirewallInfo.GetAsync(CancellationToken c) => Task.FromResult((FirewallFacts)(object)Facts!);

    Task<EncryptionFacts> IEncryptionInfo.GetAsync(CancellationToken c) => Task.FromResult((EncryptionFacts)(object)Facts!);

    Task<SecureBootTpmFacts> ISecureBootTpmInfo.GetAsync(CancellationToken c) => Task.FromResult((SecureBootTpmFacts)(object)Facts!);

    Task<Smb1Facts> ISmb1Info.GetAsync(CancellationToken c) => Task.FromResult((Smb1Facts)(object)Facts!);
}

/// <summary>Every check, fed with healthy facts from a typical Windows 11 home PC.</summary>
public static class HealthyPc
{
    public static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public static FakeRegistry Registry() => new FakeRegistry()
        .Set(RegistryRoot.LocalMachine, WindowsVersionFacts.CurrentVersionKey, "ProductName", "Windows 10 Home")
        .Set(RegistryRoot.LocalMachine, WindowsVersionFacts.CurrentVersionKey, "EditionID", "Core")
        .Set(RegistryRoot.LocalMachine, WindowsVersionFacts.CurrentVersionKey, "DisplayVersion", "25H2")
        .Set(RegistryRoot.LocalMachine, WindowsVersionFacts.CurrentVersionKey, "CurrentBuild", "26200")
        .Set(RegistryRoot.LocalMachine, UacCheck.PolicyKey, "EnableLUA", 1)
        .Set(RegistryRoot.LocalMachine, UacCheck.PolicyKey, "ConsentPromptBehaviorAdmin", 5)
        .Set(RegistryRoot.LocalMachine, RemoteDesktopCheck.TerminalServerKey, "fDenyTSConnections", 1)
        .Set(RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients", "{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}", new[] { "C: volume" })
        .Set(RegistryRoot.CurrentUser, @"Software\Microsoft\OneDrive\Accounts\Personal", "UserFolder", @"C:\Users\Pat\OneDrive")
        .Set(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", "Personal", @"C:\Users\Pat\OneDrive\Documents");

    public static CheckSources Sources(FakeRegistry? registry = null, params string[] running) => new(
        registry ?? Registry(),
        new FakeProcesses(running),
        new FakeWindowsUpdate { Status = new WindowsUpdateStatus(ServiceStart.Manual, Now.AddDays(-5), false, null), Waiting = 0 },
        new FakeFacts<AntivirusFacts>(new AntivirusFacts([new AntivirusProduct("Windows Defender", true, true)], new DefenderStatus(true, true, Now.AddHours(-6), "Normal"))),
        new FakeFacts<FirewallFacts>(new FirewallFacts(new Dictionary<NetworkProfile, bool> { [NetworkProfile.Domain] = true, [NetworkProfile.Private] = true, [NetworkProfile.Public] = true }, [])),
        new FakeFacts<EncryptionFacts>(new EncryptionFacts(true, EncryptionState.Encrypted, true)),
        new FakeFacts<SecureBootTpmFacts>(new SecureBootTpmFacts(SecureBootState.On, true, true, "2.0")),
        new FakeFacts<Smb1Facts>(new Smb1Facts(FeatureState.NotPresent, false)),
        RemoteToolList.LoadEmbedded(),
        new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now));
}
