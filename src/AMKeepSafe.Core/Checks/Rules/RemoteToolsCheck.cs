using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Core.Checks.Rules;

/// <summary>
/// Lists remote control programs, installed or open. Information only: these have honest uses,
/// but scammers often ask people to install them, so people are asked whether they recognise them.
/// </summary>
public sealed class RemoteToolsCheck(RemoteToolList list, IRegistryReader registry, IProcessList processes) : ISafetyCheck
{
    public const string CheckId = "remote-access-tools";

    public const string NoneFound = "noneFound";
    public const string Found = "found";

    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>How each found tool is listed. From checks.json.</summary>
    public sealed record Settings
    {
        public string ItemInstalled { get; init; } = "{name}: installed";

        public string ItemOpen { get; init; } = "{name}: open right now";

        public string ItemInstalledAndOpen { get; init; } = "{name}: installed and open right now";
    }

    public string Id => CheckId;

    public IReadOnlyCollection<string> OutcomeCodes => [NoneFound, Found];

    public Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken)
    {
        var found = list.Find(InstalledProgramNames(registry), processes.RunningProgramNames());
        return Task.FromResult(Evaluate(found, definition.ParametersAs<Settings>()));
    }

    /// <summary>The names shown in Windows' list of installed apps, for every user and both 32 and 64-bit programs.</summary>
    public static IReadOnlyList<string> InstalledProgramNames(IRegistryReader registry)
    {
        var names = new List<string>();
        foreach (var (root, use32) in new[] { (RegistryRoot.LocalMachine, false), (RegistryRoot.LocalMachine, true), (RegistryRoot.CurrentUser, false) })
        {
            foreach (var sub in registry.GetSubKeyNames(root, UninstallKey, use32))
            {
                if (registry.GetString(root, $@"{UninstallKey}\{sub}", "DisplayName", use32) is { Length: > 0 } name)
                {
                    names.Add(name);
                }
            }
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static CheckOutcome Evaluate(IReadOnlyList<FoundRemoteTool> found, Settings settings)
    {
        if (found.Count == 0)
        {
            return new CheckOutcome(NoneFound, Detail: "No remote control programs found.");
        }

        var items = found.Select(t =>
        {
            var template = (t.Installed, t.Open) switch
            {
                (true, true) => settings.ItemInstalledAndOpen,
                (false, true) => settings.ItemOpen,
                _ => settings.ItemInstalled,
            };
            return CheckCatalog.Fill(template, new Dictionary<string, string> { ["name"] = t.Name });
        }).ToList();

        return new CheckOutcome(Found, Items: items, Detail: $"Found {found.Count}.");
    }
}
