using System.Globalization;
using System.Xml.Linq;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Repository;

/// <summary>
/// Accessibility: text colours must have at least 4.5:1 contrast with what they sit on (WCAG AA).
/// Reads the real Theme/Colors.xaml, so changing a colour can't quietly make text hard to read.
/// </summary>
public class ThemeContrastTests
{
    private static readonly Dictionary<string, string> Brushes = ReadBrushes();

    // Brushes either give a colour directly or point at a named <Color>, as the brand colours do.
    private static Dictionary<string, string> ReadBrushes()
    {
        var elements = XDocument.Parse(RepoFiles.Read("src", "AMKeepSafe", "Theme", "Colors.xaml")).Descendants().ToList();
        static string Key(XElement e) => e.Attributes().First(a => a.Name.LocalName == "Key").Value;

        var colours = elements.Where(e => e.Name.LocalName == "Color").ToDictionary(Key, e => e.Value.Trim());
        var brushes = new Dictionary<string, string>();
        foreach (var brush in elements.Where(e => e.Name.LocalName == "SolidColorBrush"))
        {
            var value = brush.Attribute("Color")!.Value;
            brushes[Key(brush)] = value.StartsWith("{StaticResource ", StringComparison.Ordinal)
                ? colours[value["{StaticResource ".Length..^1]]
                : value;
        }

        return brushes;
    }

    [Theory]
    [InlineData("StatusGoodBrush", "SurfaceBrush")]
    [InlineData("StatusGoodBrush", "StatusGoodBackgroundBrush")]
    [InlineData("StatusAttentionBrush", "SurfaceBrush")]
    [InlineData("StatusAttentionBrush", "StatusAttentionBackgroundBrush")]
    [InlineData("StatusProblemBrush", "SurfaceBrush")]
    [InlineData("StatusProblemBrush", "StatusProblemBackgroundBrush")]
    [InlineData("StatusInfoBrush", "SurfaceBrush")]
    [InlineData("StatusInfoBrush", "StatusInfoBackgroundBrush")]
    [InlineData("StatusUnknownBrush", "SurfaceBrush")]
    [InlineData("StatusUnknownBrush", "StatusUnknownBackgroundBrush")]
    [InlineData("TextBrush", "LightBrush")]
    [InlineData("TextBrush", "SurfaceBrush")]
    [InlineData("MutedTextBrush", "LightBrush")]
    [InlineData("LinkBrush", "SurfaceBrush")]
    [InlineData("OnDarkTextBrush", "DarkBrush")]
    [InlineData("OnDarkMutedTextBrush", "DarkBrush")]
    [InlineData("DarkBrush", "MintBrush")]
    public void Text_is_readable_on_its_background(string text, string background)
    {
        var ratio = Contrast(Colour(text), Colour(background));

        Assert.True(ratio >= 4.5, $"{text} on {background} is {ratio:0.00}:1, below 4.5:1");
    }

    [Theory]
    [InlineData("StatusGoodBrush")]
    [InlineData("StatusAttentionBrush")]
    [InlineData("StatusProblemBrush")]
    [InlineData("StatusInfoBrush")]
    [InlineData("StatusUnknownBrush")]
    public void White_symbols_inside_status_icons_are_readable(string brush)
    {
        Assert.True(Contrast("#FFFFFF", Colour(brush)) >= 4.5);
    }

    [Fact]
    public void Red_and_amber_look_different_from_each_other_and_from_green()
    {
        // They're also told apart by icon shape and words, but shouldn't be near-identical colours.
        Assert.NotEqual(Colour("StatusProblemBrush"), Colour("StatusAttentionBrush"));
        Assert.True(Distance(Colour("StatusProblemBrush"), Colour("StatusGoodBrush")) > 100);
        Assert.True(Distance(Colour("StatusAttentionBrush"), Colour("StatusGoodBrush")) > 100);
    }

    private static string Colour(string key) => Brushes.TryGetValue(key, out var c) ? c : throw new KeyNotFoundException($"{key} isn't in Colors.xaml");

    private static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        static double Channel(int c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));
    }

    private static double Distance(string a, string b)
    {
        var (r1, g1, b1) = Rgb(a);
        var (r2, g2, b2) = Rgb(b);
        return Math.Sqrt(Math.Pow(r1 - r2, 2) + Math.Pow(g1 - g2, 2) + Math.Pow(b1 - b2, 2));
    }

    private static (int R, int G, int B) Rgb(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8)
        {
            h = h[2..];
        }

        return (int.Parse(h[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(h[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(h[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }
}
