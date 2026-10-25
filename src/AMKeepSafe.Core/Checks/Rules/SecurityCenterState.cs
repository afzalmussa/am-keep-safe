namespace AMKeepSafe.Core.Checks.Rules;

/// <summary>
/// Windows Security Center reports each antivirus or firewall product's state as one number.
/// The middle byte says whether it's on (0x10 or 0x11) and the low byte whether its virus
/// information is up to date (0x00) or old (0x10). For example 0x061100 is "on and up to date".
/// </summary>
public static class SecurityCenterState
{
    public static bool IsOn(int productState) => ((productState >> 8) & 0xFF) is 0x10 or 0x11;

    public static bool IsUpToDate(int productState) => (productState & 0xFF) == 0x00;
}
