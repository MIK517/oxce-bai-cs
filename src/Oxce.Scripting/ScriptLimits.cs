namespace Oxce.Scripting;

public static class ScriptLimits
{
    public const int MaximumOutputs = 9;
    public const int MaximumArguments = 16;
    public const int RegisterPointerFactor = 64;
    public const int DefaultMaximumInstructions = 65_536;
    public const int DefaultMaximumExecutionSteps = 1_000_000;
    public const int DefaultMaximumTraceEntries = 10_000;

    public static int MaximumRegisterBytes => checked(RegisterPointerFactor * IntPtr.Size);
}
