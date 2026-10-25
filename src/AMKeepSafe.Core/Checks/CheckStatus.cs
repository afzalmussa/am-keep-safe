namespace AMKeepSafe.Core.Checks;

/// <summary>How a check came out. Shown with a colour, an icon and a label, never colour alone.</summary>
public enum CheckStatus
{
    /// <summary>Red: worth fixing soon. The fix is always free (trust rule 2).</summary>
    Problem,

    /// <summary>Amber: worth a look.</summary>
    Attention,

    /// <summary>The check couldn't finish, for example Windows didn't answer.</summary>
    Unknown,

    /// <summary>Information only, not a pass or fail.</summary>
    Info,

    /// <summary>Green: nothing to do.</summary>
    Good,
}

public static class CheckStatusExtensions
{
    /// <summary>Red and amber. Never shown with a Pro prompt (trust rule 2).</summary>
    public static bool NeedsAction(this CheckStatus status) => status is CheckStatus.Problem or CheckStatus.Attention;
}
