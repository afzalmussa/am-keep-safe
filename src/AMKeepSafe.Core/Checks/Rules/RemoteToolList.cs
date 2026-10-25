using System.Text.Json;

namespace AMKeepSafe.Core.Checks.Rules;

/// <summary>A remote control program, from data/remote-tools.json.</summary>
public sealed record RemoteTool
{
    public required string Name { get; init; }

    /// <summary>Text to look for in the names of installed programs, ignoring capitals.</summary>
    public IReadOnlyList<string> InstalledNames { get; init; } = [];

    /// <summary>Installed programs containing this text are something else, for example a VPN from the same company.</summary>
    public IReadOnlyList<string> NotNames { get; init; } = [];

    /// <summary>Program file names to look for among what's running, such as "AnyDesk.exe". Catches copies that run without installing.</summary>
    public IReadOnlyList<string> Programs { get; init; } = [];

    /// <summary>Built into Windows, so only worth mentioning while it's open.</summary>
    public bool OnlyWhenOpen { get; init; }
}

public sealed class RemoteToolList
{
    public const string ResourceName = "AMKeepSafe.Core.Data.remote-tools.json";

    public required IReadOnlyList<RemoteTool> Tools { get; init; }

    public static RemoteToolList LoadEmbedded()
    {
        using var stream = typeof(RemoteToolList).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded file '{ResourceName}' is missing.");
        return Load(stream);
    }

    public static RemoteToolList Load(Stream json)
    {
        var file = JsonSerializer.Deserialize<FileShape>(json, CheckCatalog.JsonOptions)
            ?? throw new InvalidDataException("remote-tools.json is empty.");
        return new RemoteToolList { Tools = file.Tools ?? [] };
    }

    /// <summary>Problems that would stop a tool ever being found. Empty means it's fine.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        foreach (var tool in Tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                problems.Add("A tool has no name.");
            }

            if (tool.InstalledNames.Count == 0 && tool.Programs.Count == 0)
            {
                problems.Add($"{tool.Name} has nothing to look for: add installedNames or programs.");
            }

            if (tool.OnlyWhenOpen && tool.Programs.Count == 0)
            {
                problems.Add($"{tool.Name} is only shown when open, so it needs at least one program name.");
            }

            foreach (var program in tool.Programs.Where(p => !p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"{tool.Name}: program '{program}' should be a file name ending in .exe.");
            }
        }

        foreach (var duplicate in Tools.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            problems.Add($"{duplicate.Key} is listed more than once.");
        }

        return problems;
    }

    /// <summary>Which tools are installed or open. Never judges them: they have honest uses too.</summary>
    public IReadOnlyList<FoundRemoteTool> Find(IEnumerable<string> installedProgramNames, IEnumerable<string> runningProgramNames)
    {
        var installed = installedProgramNames.ToList();
        var running = new HashSet<string>(runningProgramNames, StringComparer.OrdinalIgnoreCase);

        var found = new List<FoundRemoteTool>();
        foreach (var tool in Tools)
        {
            var isOpen = tool.Programs.Any(running.Contains);
            var isInstalled = !tool.OnlyWhenOpen && installed.Any(name =>
                tool.InstalledNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase))
                && !tool.NotNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase)));

            if (isOpen || isInstalled)
            {
                found.Add(new FoundRemoteTool(tool.Name, isInstalled, isOpen));
            }
        }

        return found;
    }

    private sealed record FileShape
    {
        public List<RemoteTool>? Tools { get; init; }
    }
}

public sealed record FoundRemoteTool(string Name, bool Installed, bool Open);
