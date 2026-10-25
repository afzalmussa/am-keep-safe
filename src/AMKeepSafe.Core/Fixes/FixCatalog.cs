using System.Text.Json;
using AMKeepSafe.Core.Checks;

namespace AMKeepSafe.Core.Fixes;

/// <summary>Everything the fixes and undo say, loaded from data/fixes.json.</summary>
public sealed class FixCatalog
{
    public const string ResourceName = "AMKeepSafe.Core.Data.fixes.json";

    public required FixCommonText Common { get; init; }

    public required IReadOnlyList<FixDefinition> Fixes { get; init; }

    public FixDefinition this[string id] =>
        Fixes.FirstOrDefault(f => f.Id == id) ?? throw new KeyNotFoundException($"There's no fix called '{id}' in fixes.json.");

    /// <summary>The fixes to offer under a result. Empty for anything that isn't fixable.</summary>
    public IReadOnlyList<FixDefinition> For(string checkId, string outcomeCode) =>
        Fixes.Where(f => f.CheckId == checkId && f.ForOutcomes.Contains(outcomeCode)).ToList();

    public static FixCatalog LoadEmbedded()
    {
        using var stream = typeof(FixCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded file '{ResourceName}' is missing.");
        return Load(stream);
    }

    public static FixCatalog Load(Stream json)
    {
        var file = JsonSerializer.Deserialize<FileShape>(json, CheckCatalog.JsonOptions)
            ?? throw new InvalidDataException("fixes.json is empty.");
        return new FixCatalog
        {
            Common = file.Common ?? throw new InvalidDataException("fixes.json has no common section."),
            Fixes = file.Fixes ?? [],
        };
    }

    /// <summary>Problems that would show up as missing words. Empty means it's fine.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        foreach (var duplicate in Fixes.GroupBy(f => f.Id).Where(g => g.Count() > 1))
        {
            problems.Add($"The fix id '{duplicate.Key}' is used more than once.");
        }

        foreach (var fix in Fixes)
        {
            foreach (var (name, text) in new[] { ("buttonLabel", fix.ButtonLabel), ("title", fix.Title), ("doneText", fix.DoneText), ("failedText", fix.FailedText) })
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    problems.Add($"Fix '{fix.Id}' has no {name}.");
                }
            }

            if (fix.ForOutcomes.Count == 0)
            {
                problems.Add($"Fix '{fix.Id}' isn't offered for any outcome.");
            }

            foreach (var (key, change) in fix.Changes)
            {
                if (string.IsNullOrWhiteSpace(change.Text))
                {
                    problems.Add($"Fix '{fix.Id}' change '{key}' has no text.");
                }
            }
        }

        return problems;
    }

    private sealed record FileShape
    {
        public FixCommonText? Common { get; init; }

        public List<FixDefinition>? Fixes { get; init; }
    }
}

public sealed record FixCommonText
{
    public required string PreviewIntro { get; init; }

    public required string DryRunIntro { get; init; }

    public required string ConfirmButton { get; init; }

    public required string DryRunConfirmButton { get; init; }

    public required string CancelButton { get; init; }

    public required string DetailsLabel { get; init; }

    public required string UndoInApp { get; init; }

    public required string UndoViaSettings { get; init; }

    public required string UndoNotNeeded { get; init; }

    public required string RestartNeeded { get; init; }

    public required string NothingToDo { get; init; }

    public required string DryRunDone { get; init; }

    public required string UndoTitle { get; init; }

    public required string UndoIntro { get; init; }

    public required string UndoConfirmButton { get; init; }

    public required string UndoDone { get; init; }

    public required string UndoFailed { get; init; }

    public required string ChangedSinceNote { get; init; }

    public required string DoneTitle { get; init; }

    public required string PartlyDoneTitle { get; init; }

    public required string PartlyDoneText { get; init; }

    public required string FailedTitle { get; init; }

    public required string DryRunTitle { get; init; }

    public required string NothingToDoTitle { get; init; }

    public required string WorkingText { get; init; }

    public required string UndoWindowTitle { get; init; }

    public required string UndoWindowIntro { get; init; }

    public required string UndoEmpty { get; init; }

    /// <summary>Has a {date} placeholder.</summary>
    public required string UndoneOn { get; init; }

    public required string UndoButton { get; init; }

    public required string ViaSettingsNote { get; init; }

    public required string OpenSettingsButton { get; init; }

    public required string CloseButton { get; init; }
}

public sealed record FixDefinition
{
    public required string Id { get; init; }

    public required string CheckId { get; init; }

    /// <summary>The check outcomes this fix is offered for.</summary>
    public IReadOnlyList<string> ForOutcomes { get; init; } = [];

    public required string ButtonLabel { get; init; }

    /// <summary>The preview window's heading, for example "Turn the firewall back on".</summary>
    public required string Title { get; init; }

    /// <summary>Wording for each change the fix can make, by key.</summary>
    public IReadOnlyDictionary<string, ChangeText> Changes { get; init; } = new Dictionary<string, ChangeText>();

    public required string DoneText { get; init; }

    public required string FailedText { get; init; }

    public ChangeText TextFor(string key) =>
        Changes.TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"Fix '{Id}' has no wording for change '{key}' in fixes.json.");
}

/// <param name="Text">What the change does.</param>
/// <param name="UndoText">What undoing it does. Only needed for changes that can be undone in the app.</param>
public sealed record ChangeText(string Text, string? UndoText = null);
