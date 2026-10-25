using System.Text.RegularExpressions;

namespace AMKeepSafe.Core.Wording;

public enum WordingProblem
{
    /// <summary>Says or implies the PC is safe, protected or secure. The app is not an antivirus (trust rule 3).</summary>
    OverPromise,

    /// <summary>Words that frighten rather than inform, such as "threat", "danger" or "urgent" (trust rule 1).</summary>
    ScareWord,

    /// <summary>"3 threats found" style counters (trust rule 1).</summary>
    Counter,

    /// <summary>Exclamation marks shout. Calm text doesn't need them (trust rule 1).</summary>
    Exclamation,

    /// <summary>House style: no em or en dashes.</summary>
    Dash,

    /// <summary>Technical words that mean nothing to the people this app is for.</summary>
    Jargon,
}

public sealed record WordingViolation(WordingProblem Problem, string Match)
{
    public override string ToString() => $"{Problem}: \"{Match}\"";
}

/// <summary>
/// The wording rules every piece of user-facing text must pass. The tests run every string in the
/// app's screens, data files and text classes through <see cref="Check"/>, so a scary or
/// over-promising sentence fails the build instead of reaching a customer.
/// </summary>
public static class WordingRules
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex OverPromise = new(
        @"\b(?:is|are|be|been|being|stay|stays|now|fully|completely|totally|perfectly|always)\s+(?:safe|protected|secure)\b"
        + @"|\b(?:you're|you\s+are|you'll\s+be)\s+(?:safe|protected|secure)\b"
        + @"|\b(?:keeps?|keeping)\s+(?:you|your\s+\w+)\s+(?:safe|protected|secure)\b"
        + @"|100\s*%"
        + @"|\bguarantee",
        Options);

    private static readonly Regex ScareWord = new(
        @"\b(?:threats?|infected|infections?|danger|dangerous|urgent|urgently|immediately|act\s+now|hurry"
        + @"|critical|severe|alarming|at\s+risk|hacked|compromised|catastrophic|disaster)\b",
        Options);

    // A number (or a {placeholder} that will become one) followed by a scary noun, like "3 threats found".
    private static readonly Regex Counter = new(
        @"(?:\b\d+|\{[^}]*\})\s+(?:threats?|problems?|issues?|risks?|errors?|viruses|dangers?)\s+(?:found|detected)\b",
        Options);

    private static readonly Regex Jargon = new(
        @"\b(?:PowerShell|registry|WMI|CIM|HRESULT|exception|stack\s+trace|null)\b|\b0x[0-9a-f]+\b",
        Options);

    /// <summary>Checks one piece of text. An empty list means it passes.</summary>
    /// <param name="includeJargon">
    /// False for text that is allowed to be technical, such as the exact change shown under "Details"
    /// in a fix preview. Everything else still applies.
    /// </param>
    public static IReadOnlyList<WordingViolation> Check(string? text, bool includeJargon = true)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var found = new List<WordingViolation>();
        Add(found, WordingProblem.OverPromise, OverPromise, text);
        Add(found, WordingProblem.ScareWord, ScareWord, text);
        Add(found, WordingProblem.Counter, Counter, text);

        if (text.Contains('!'))
        {
            found.Add(new WordingViolation(WordingProblem.Exclamation, "!"));
        }

        foreach (var dash in new[] { '\u2014', '\u2013' })
        {
            if (text.Contains(dash))
            {
                found.Add(new WordingViolation(WordingProblem.Dash, dash.ToString()));
            }
        }

        if (includeJargon)
        {
            Add(found, WordingProblem.Jargon, Jargon, text);
        }

        return found;
    }

    public static bool Passes(string? text, bool includeJargon = true) => Check(text, includeJargon).Count == 0;

    private static void Add(List<WordingViolation> found, WordingProblem problem, Regex pattern, string text)
    {
        foreach (Match match in pattern.Matches(text))
        {
            found.Add(new WordingViolation(problem, match.Value));
        }
    }
}
