using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AMKeepSafe.Core.Configuration;

namespace AMKeepSafe.Core.Checks;

/// <summary>
/// Everything the Safety check says, loaded from data/checks.json: the checks, the colour of each
/// outcome, the words and the help steps. Edit the JSON to change wording without touching code.
/// </summary>
public sealed partial class CheckCatalog
{
    public const string ResourceName = "AMKeepSafe.Core.Data.checks.json";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public required IReadOnlyDictionary<CheckStatus, string> StatusLabels { get; init; }

    public required SummaryText Summary { get; init; }

    /// <summary>Used for any check that fails to finish.</summary>
    public required OutcomeDefinition CouldNotCheck { get; init; }

    public required IReadOnlyList<CheckDefinition> Checks { get; init; }

    public CheckDefinition this[string id] =>
        Checks.FirstOrDefault(c => c.Id == id) ?? throw new KeyNotFoundException($"There's no check called '{id}' in checks.json.");

    public static CheckCatalog LoadEmbedded()
    {
        using var stream = typeof(CheckCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded file '{ResourceName}' is missing.");
        return Load(stream);
    }

    /// <summary>Reads a catalog. Throws if the file is broken, because the app can't explain results without it.</summary>
    public static CheckCatalog Load(Stream json)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(json, JsonOptions)
            ?? throw new InvalidDataException("checks.json is empty.");

        return new CheckCatalog
        {
            StatusLabels = file.StatusLabels ?? throw new InvalidDataException("checks.json has no statusLabels."),
            Summary = file.Summary ?? throw new InvalidDataException("checks.json has no summary."),
            CouldNotCheck = file.CouldNotCheck ?? throw new InvalidDataException("checks.json has no couldNotCheck."),
            Checks = file.Checks ?? [],
        };
    }

    /// <summary>Applies the wording for an outcome. An outcome code with no wording counts as "couldn't check".</summary>
    public CheckResult Render(CheckDefinition check, CheckOutcome outcome)
    {
        if (!check.Outcomes.TryGetValue(outcome.Code, out var wording))
        {
            return RenderCouldNotCheck(check);
        }

        return new CheckResult(
            check.Id,
            check.Title,
            outcome.Code,
            wording.Status,
            Fill(wording.Summary, outcome.Values),
            wording.HowToFix.Select(step => Fill(step, outcome.Values)).ToList(),
            wording.Actions,
            outcome.Items);
    }

    public CheckResult RenderCouldNotCheck(CheckDefinition check) =>
        new(check.Id, check.Title, CouldNotCheckCode, CouldNotCheck.Status, CouldNotCheck.Summary, CouldNotCheck.HowToFix, CouldNotCheck.Actions, []);

    public const string CouldNotCheckCode = "couldNotCheck";

    /// <summary>Replaces {name} with its value. Unknown placeholders are left in, so the tests can catch them.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);

    public static IEnumerable<string> PlaceholdersIn(string text) => Placeholder().Matches(text).Select(m => m.Groups[1].Value);

    /// <summary>Problems with the file that would show up as broken text or buttons. Empty means it's fine.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        foreach (var status in Enum.GetValues<CheckStatus>())
        {
            if (!StatusLabels.TryGetValue(status, out var label) || string.IsNullOrWhiteSpace(label))
            {
                problems.Add($"statusLabels has no label for {status}.");
            }
        }

        foreach (var duplicate in Checks.GroupBy(c => c.Id).Where(g => g.Count() > 1))
        {
            problems.Add($"The check id '{duplicate.Key}' is used more than once.");
        }

        ValidateOutcome("couldNotCheck", CouldNotCheck, problems);

        foreach (var check in Checks)
        {
            if (string.IsNullOrWhiteSpace(check.Id) || string.IsNullOrWhiteSpace(check.Title) || string.IsNullOrWhiteSpace(check.WhatWeCheck))
            {
                problems.Add($"Check '{check.Id}' needs an id, a title and whatWeCheck.");
            }

            if (check.Outcomes.Count == 0)
            {
                problems.Add($"Check '{check.Id}' has no outcomes.");
            }

            foreach (var (code, outcome) in check.Outcomes)
            {
                ValidateOutcome($"{check.Id}.{code}", outcome, problems);
            }
        }

        return problems;
    }

    private static void ValidateOutcome(string where, OutcomeDefinition outcome, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(outcome.Summary))
        {
            problems.Add($"{where} has no summary.");
        }

        foreach (var action in outcome.Actions)
        {
            if (string.IsNullOrWhiteSpace(action.Label))
            {
                problems.Add($"{where} has a button with no label.");
            }

            if ((action.SettingsPage is null) == (action.Url is null))
            {
                problems.Add($"{where} button '{action.Label}' must have either a settingsPage or a url, not both or neither.");
            }

            if (action.Url is not null && !AppSettings.IsUsableUrl(action.Url))
            {
                problems.Add($"{where} button '{action.Label}' has a url that isn't a secure https address.");
            }
        }
    }

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)\}")]
    private static partial Regex Placeholder();

    private sealed record CatalogFile
    {
        public Dictionary<CheckStatus, string>? StatusLabels { get; init; }

        public SummaryText? Summary { get; init; }

        public OutcomeDefinition? CouldNotCheck { get; init; }

        public List<CheckDefinition>? Checks { get; init; }
    }
}

/// <summary>The headline above the results.</summary>
public sealed record SummaryText
{
    /// <summary>Every check came out green or information only. Never "your PC is safe" (trust rule 3).</summary>
    public required string AllClear { get; init; }

    public required string SomeToLookAt { get; init; }

    /// <summary>Added when one or more checks couldn't finish.</summary>
    public required string SomeNotChecked { get; init; }

    public required string WhatWeCheckedLink { get; init; }
}

public sealed record CheckDefinition
{
    public required string Id { get; init; }

    /// <summary>Short name shown on the result, for example "Windows updates".</summary>
    public required string Title { get; init; }

    /// <summary>One sentence for the "See what we checked" list.</summary>
    public required string WhatWeCheck { get; init; }

    /// <summary>Numbers and dates the check's rules use, such as how many days count as "a while".</summary>
    public JsonElement Parameters { get; init; }

    public IReadOnlyDictionary<string, OutcomeDefinition> Outcomes { get; init; } = new Dictionary<string, OutcomeDefinition>();

    /// <summary>Reads <see cref="Parameters"/> as the check's own settings type.</summary>
    public T ParametersAs<T>() where T : new() =>
        Parameters.ValueKind == JsonValueKind.Object
            ? Parameters.Deserialize<T>(CheckCatalog.JsonOptions) ?? new T()
            : new T();
}

public sealed record OutcomeDefinition
{
    public required CheckStatus Status { get; init; }

    /// <summary>One calm sentence (or two) saying what this means.</summary>
    public required string Summary { get; init; }

    /// <summary>Simple steps, shown under "How to fix".</summary>
    public IReadOnlyList<string> HowToFix { get; init; } = [];

    public IReadOnlyList<CheckAction> Actions { get; init; } = [];
}
