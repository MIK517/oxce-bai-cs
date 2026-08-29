namespace Oxce.Scripting;

public static class ScriptLimits
{
    public const int MaximumOutputs = 9;
    public const int MaximumArguments = 16;
    public const int RegisterPointerFactor = 64;

    public static int MaximumRegisterBytes => checked(RegisterPointerFactor * IntPtr.Size);
}
