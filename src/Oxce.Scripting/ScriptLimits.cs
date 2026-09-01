namespace Oxce.Scripting;

public static class ScriptLimits
{
    public const int MaximumOutputs = 9;
    public const int MaximumArguments = 16;
    public const int RegisterPointerFactor = 64;
    public const int DefaultMaximumInstructions = 65_536;
    public const int DefaultMaximumExecutionSteps = 1_000_000;
    public const int DefaultMaximumTraceEntries = 10_000;
    public const int DefaultMaximumCallDepth = 16;
    public const int MaximumCallDepth = 64;
    public const int MaximumGlobalEvents = 256;
    public const int EventOffsetScale = 100;
    public const int MaximumEventOffset = 100 * EventOffsetScale;
    public const int DefaultMaximumEventExecutions = 256;

    public static int MaximumRegisterBytes => checked(RegisterPointerFactor * IntPtr.Size);
}
