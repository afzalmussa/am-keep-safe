namespace AMKeepSafe.Core.Checks;

/// <summary>
/// Thrown when Windows didn't give a check what it needed, for example a command timed out.
/// The check then shows as "couldn't check" and the reason goes in the log.
/// </summary>
public sealed class CouldNotReadException(string message, Exception? inner = null) : Exception(message, inner);
