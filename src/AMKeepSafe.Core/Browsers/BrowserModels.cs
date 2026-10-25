using AMKeepSafe.Core.Platform;

namespace AMKeepSafe.Core.Browsers;

public enum Browser
{
    Edge,
    Chrome,
    Firefox,
}

/// <summary>Who chose a setting.</summary>
public enum SetBy
{
    /// <summary>Nobody changed it: it's the browser's own normal setting.</summary>
    BrowserDefault,

    /// <summary>Changed in the browser's settings, usually by the person.</summary>
    User,

    /// <summary>An add-on took it over.</summary>
    Extension,

    /// <summary>A policy setting on this PC forces it ("Managed by your organisation").</summary>
    Policy,
}

/// <param name="Name">The search engine's name, if known.</param>
/// <param name="Url">Its search address, if known.</param>
/// <param name="SetByName">The add-on that set it, when <see cref="SetBy"/> is Extension.</param>
public sealed record SearchSetting(string? Name, string? Url, SetBy SetBy, string? SetByName = null);

/// <param name="Pages">What opens when the browser starts. Empty means the browser's own start page.</param>
public sealed record StartPagesSetting(IReadOnlyList<string> Pages, SetBy SetBy, string? SetByName = null);

public enum ExtensionSource
{
    /// <summary>From the browser's official add-on store.</summary>
    Store,

    /// <summary>Added another way: by another program, from a file, or from somewhere other than the store.</summary>
    OutsideStore,

    /// <summary>Forced on by a policy setting on this PC.</summary>
    Policy,
}

/// <param name="Name">The add-on's own name, as its maker wrote it.</param>
public sealed record BrowserExtension(string Id, string Name, ExtensionSource Source, bool Enabled);

/// <summary>Everything read from one browser profile. Read only: profile files are never changed.</summary>
/// <param name="ProfileKey">The profile's folder name, for example "Default" or "Profile 1".</param>
/// <param name="ProfileName">The name the person sees, for example "Person 1" or "Work".</param>
/// <param name="ReadProblem">Set if the profile couldn't be read. The other values are then empty.</param>
public sealed record BrowserProfile(
    Browser Browser,
    string ProfileKey,
    string ProfileName,
    SearchSetting Search,
    StartPagesSetting StartPages,
    IReadOnlyList<BrowserExtension> Extensions,
    IReadOnlyList<string> NotificationSites,
    string? ReadProblem = null);

public enum PolicyPlace
{
    Registry,
    File,
}

/// <summary>One place a browser's policies are set: a registry key, or Firefox's policies.json file.</summary>
/// <param name="Settings">What's set there, one line each, for example "HomepageLocation = https://example.com".</param>
public sealed record PolicyLocation(PolicyPlace Place, RegistryRoot? Root, string Path, IReadOnlyList<string> Settings);

/// <summary>Where the browsers keep their files for the account being checked.</summary>
/// <param name="FirefoxInstallFolders">Where Firefox is installed, to find its policies.json.</param>
public sealed record BrowserLocations(string LocalAppData, string RoamingAppData, IReadOnlyList<string> FirefoxInstallFolders)
{
    public static BrowserLocations ForCurrentUser() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(f => Path.Combine(Environment.GetFolderPath(f), "Mozilla Firefox"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList());
}
