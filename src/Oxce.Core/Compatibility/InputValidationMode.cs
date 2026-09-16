namespace Oxce.Core.Compatibility;

/// <summary>
/// How game, mod and save input that the reference engine silently tolerates is handled.
/// Checks that protect the host (path escapes, size and count limits) apply in both modes.
/// See ADR 0026.
/// </summary>
public enum InputValidationMode
{
    /// <summary>Reject malformed input with an error (the default).</summary>
    Strict,

    /// <summary>
    /// Accept malformed input the way the reference engine does, for example by treating
    /// a non-canonical virtual path as a missing file or skipping out-of-range CAT entries.
    /// </summary>
    Compatibility,
}

public static class InputValidationModes
{
    /// <summary>Parses the command-line spelling (<c>strict</c> or <c>compatibility</c>).</summary>
    public static bool TryParse(string? value, out InputValidationMode mode)
    {
        switch (value)
        {
            case "strict":
                mode = InputValidationMode.Strict;
                return true;
            case "compatibility":
                mode = InputValidationMode.Compatibility;
                return true;
            default:
                mode = InputValidationMode.Strict;
                return false;
        }
    }

    public static InputValidationMode Validate(this InputValidationMode mode) => Enum.IsDefined(mode)
        ? mode
        : throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown input validation mode.");
}
