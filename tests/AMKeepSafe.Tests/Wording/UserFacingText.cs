using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Wording;

/// <param name="Source">Where it came from, so a failure says which file and line to fix.</param>
/// <param name="AllowJargon">True for text that is allowed to be technical, such as log messages.</param>
public sealed record TextSample(string Source, string Text, bool AllowJargon);

/// <summary>
/// Collects the text in the repository that users can see: the app's screens, the data files and
/// the strings in the app's code. Lines marked <c>wording-check: ignore</c> are skipped, which keeps
/// every exception visible in code review.
/// </summary>
public static partial class UserFacingText
{
    public const string IgnoreMarker = "wording-check: ignore";

    private static readonly string[] XamlTextAttributes =
        ["Text", "Content", "Header", "ToolTip", "Title", "AutomationProperties.Name", "AutomationProperties.HelpText"];

    // Keys in data files that hold ids and reference links rather than sentences people see.
    private static readonly HashSet<string> DataKeysToSkip = new(StringComparer.OrdinalIgnoreCase) { "id", "$schema", "source" };

    public static IEnumerable<TextSample> FromScreens()
    {
        foreach (var file in RepoFiles.Find(Path.Combine("src", "AMKeepSafe"), "*.xaml"))
        {
            var document = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var element in document.Descendants())
            {
                foreach (var attribute in element.Attributes())
                {
                    var name = attribute.Name.LocalName;
                    if (XamlTextAttributes.Contains(name) && !attribute.Value.TrimStart().StartsWith('{'))
                    {
                        yield return new TextSample($"{RepoFiles.Relative(file)}:{((System.Xml.IXmlLineInfo)element).LineNumber}", attribute.Value, false);
                    }
                }

                // Text written between tags, such as <Run>Some words</Run>.
                if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value) && element.Name.LocalName is "Run" or "TextBlock" or "Hyperlink")
                {
                    yield return new TextSample($"{RepoFiles.Relative(file)}:{((System.Xml.IXmlLineInfo)element).LineNumber}", element.Value, false);
                }
            }
        }
    }

    public static IEnumerable<TextSample> FromDataFiles()
    {
        foreach (var file in RepoFiles.Find("data", "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            foreach (var sample in Walk(document.RootElement, RepoFiles.Relative(file), "$"))
            {
                yield return sample;
            }
        }
    }

    /// <summary>
    /// String literals in the app and Core code. These include log messages, so technical words are
    /// allowed, but nothing may scare or over-promise. The Windows project is left out: it talks to
    /// Windows in Windows' own terms (command names, settings paths) and shows nothing to users.
    /// </summary>
    public static IEnumerable<TextSample> FromCode()
    {
        var files = RepoFiles.Find(Path.Combine("src", "AMKeepSafe"), "*.cs")
            .Concat(RepoFiles.Find(Path.Combine("src", "AMKeepSafe.Core"), "*.cs"))
            .Where(f => !f.EndsWith("WordingRules.cs", StringComparison.Ordinal));

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.Contains(IgnoreMarker, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match literal in StringLiteral().Matches(line))
                {
                    yield return new TextSample($"{RepoFiles.Relative(file)}:{i + 1}", literal.Groups["text"].Value, true);
                }
            }
        }
    }

    public static IEnumerable<TextSample> All() => FromScreens().Concat(FromDataFiles()).Concat(FromCode());

    private static IEnumerable<TextSample> Walk(JsonElement element, string file, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return new TextSample($"{file} {path}", element.GetString()!, false);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject().Where(p => !DataKeysToSkip.Contains(p.Name)))
                {
                    foreach (var sample in Walk(property.Value, file, $"{path}.{property.Name}"))
                    {
                        yield return sample;
                    }
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var sample in Walk(item, file, $"{path}[{index++}]"))
                    {
                        yield return sample;
                    }
                }

                break;
        }
    }

    // A normal or interpolated C# string on one line. Good enough for this codebase's style.
    [GeneratedRegex("""(?<!')"(?<text>(?:[^"\\]|\\.)*)"(?!')""")]
    private static partial Regex StringLiteral();
}
