using System.Globalization;

namespace AMKeepSafe.Core.Text;

/// <summary>Dates and times written the way people say them in the UK.</summary>
public static class FriendlyText
{
    private static readonly CultureInfo UkEnglish = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>For example "13 October 2026".</summary>
    public static string Date(DateTimeOffset when) => when.ToString("d MMMM yyyy", UkEnglish);

    public static string Date(DateOnly when) => when.ToString("d MMMM yyyy", UkEnglish);

    /// <summary>For example "25 October 2026 at 2:05pm", in the PC's own time zone.</summary>
    public static string DateAndTime(DateTimeOffset when)
    {
        var local = when.ToLocalTime();
        return $"{local.ToString("d MMMM yyyy", UkEnglish)} at {local.ToString("h:mm", UkEnglish)}{(local.Hour < 12 ? "am" : "pm")}";
    }

    /// <summary>For example "today", "yesterday", "3 days ago", "5 weeks ago".</summary>
    public static string Ago(DateTimeOffset when, DateTimeOffset now)
    {
        var days = (int)Math.Floor((now.ToLocalTime().Date - when.ToLocalTime().Date).TotalDays);
        return days switch
        {
            <= 0 => "today",
            1 => "yesterday",
            < 14 => $"{days} days ago",
            < 60 => $"{days / 7} weeks ago",
            < 730 => $"{days / 30} months ago",
            _ => $"{days / 365} years ago",
        };
    }

    /// <summary>"A, B and C".</summary>
    public static string List(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };
}
