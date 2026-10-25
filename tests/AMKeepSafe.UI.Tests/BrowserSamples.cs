using System.IO;
using AMKeepSafe.Core.Browsers;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Fixes;
using AMKeepSafe.Core.Platform;
using AMKeepSafe.Services;
using AMKeepSafe.ViewModels;

namespace AMKeepSafe.UI.Tests;

/// <summary>Registry values held in memory, for the browser policy reader.</summary>
internal sealed class MemoryRegistry : IRegistryReader
{
    private readonly Dictionary<string, Dictionary<string, object>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public MemoryRegistry Set(RegistryRoot root, string key, string name, object value)
    {
        if (!_keys.TryGetValue($@"{root}\{key}", out var values))
        {
            _keys[$@"{root}\{key}"] = values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        values[name] = value;
        return this;
    }

    public void RemoveKey(RegistryRoot root, string key)
    {
        foreach (var path in _keys.Keys.Where(k => k.Equals($@"{root}\{key}", StringComparison.OrdinalIgnoreCase)
                                                   || k.StartsWith($@"{root}\{key}\", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _keys.Remove(path);
        }
    }

    public object? GetValue(RegistryRoot root, string key, string name, bool use32BitView = false) =>
        _keys.TryGetValue($@"{root}\{key}", out var values) && values.TryGetValue(name, out var value) ? value : null;

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string key, bool use32BitView = false)
    {
        var prefix = $@"{root}\{key}\";
        return _keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> GetValueNames(RegistryRoot root, string key, bool use32BitView = false) =>
        _keys.TryGetValue($@"{root}\{key}", out var values) ? values.Keys.ToList() : [];
}

internal sealed class FixedPcContext(PcContext context) : IPcContextInfo
{
    public Task<PcContext> GetAsync(CancellationToken cancellationToken) => Task.FromResult(context);
}

/// <summary>
/// A Browser check over a temporary folder with one Chrome profile, and settings on this PC held in
/// memory. The real readers, findings, wording, fixes and undo run on top.
/// </summary>
internal sealed class BrowserSamples : IDisposable
{
    public const string ChromePolicies = @"SOFTWARE\Policies\Google\Chrome";

    private readonly string _root = Directory.CreateTempSubdirectory("amkeepsafe-ui-browsers-").FullName;

    public BrowserSamples(string chromePreferences = "{}", PcContext? context = null, bool dryRun = false)
    {
        var profile = Path.Combine(_root, "Local", "Google", "Chrome", "User Data", "Default");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "Preferences"), chromePreferences);
        File.WriteAllText(Path.Combine(profile, "..", "Local State"), """{ "profile": { "info_cache": { "Default": { "name": "Pat" } } } }""");

        var locations = new BrowserLocations(Path.Combine(_root, "Local"), Path.Combine(_root, "Roaming"), []);
        var policies = new BrowserPolicyReader(Registry, locations);
        Runner = new BrowserCheckRunner(
            new ChromiumReader(locations),
            new FirefoxReader(locations),
            policies,
            new FixedPcContext(context ?? new PcContext(false, @"PC\Pat", @"PC\Pat")),
            SearchEngineList.LoadEmbedded(),
            KnownBadExtensionList.LoadEmbedded(),
            BrowserText.LoadEmbedded(),
            Log);

        // Removing a policy key in the fake changer removes it from the fake registry too, so checking again sees the change.
        Changer.AfterApply = change =>
        {
            if (change is { Kind: ChangeKind.RegistryKey, To: SettingChange.Absent, Root: { } root, RegistryKey: { } key })
            {
                Registry.RemoveKey(root, key);
            }
        };

        var runner = new FixRunner(Changer, Journal, Log, TimeProvider.System, dryRun, Path.Combine(_root, "backups"));
        Coordinator = new FixCoordinator(FixSet.Create(Changer, "C:", policies), FixCatalog.LoadEmbedded(), runner, Dialogs, Log);
    }

    public MemoryRegistry Registry { get; } = new();

    public FakeChanger Changer { get; } = new();

    public MemoryJournal Journal { get; } = new();

    public FakeDialogs Dialogs { get; } = new();

    public FakeShell Shell { get; } = new();

    public FakeLog Log { get; } = new();

    public FakeBrowserProcesses Processes { get; } = new();

    public BrowserCheckRunner Runner { get; }

    public FixCoordinator Coordinator { get; }

    public List<AppTab> TabsVisited { get; } = [];

    public BrowserCheckViewModel ViewModel(bool withFixes = true) =>
        new(Runner, Shell, Dialogs, Log, withFixes ? Coordinator : null, Processes, TabsVisited.Add);

    /// <summary>A Browser check tab for a PC where no browsers have been used, for tests about other things.</summary>
    public static BrowserCheckViewModel NoBrowsers()
    {
        var nowhere = Path.Combine(Path.GetTempPath(), "amkeepsafe-ui-no-browsers-" + Guid.NewGuid().ToString("N"));
        var locations = new BrowserLocations(nowhere, nowhere, []);
        var log = new FakeLog();
        var runner = new BrowserCheckRunner(
            new ChromiumReader(locations),
            new FirefoxReader(locations),
            new BrowserPolicyReader(new MemoryRegistry(), locations),
            new FixedPcContext(new PcContext(false, @"PC\Pat", @"PC\Pat")),
            SearchEngineList.LoadEmbedded(),
            KnownBadExtensionList.LoadEmbedded(),
            BrowserText.LoadEmbedded(),
            log);
        return new BrowserCheckViewModel(runner, new FakeShell(), new FakeDialogs(), log);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
