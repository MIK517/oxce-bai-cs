namespace Oxce.Platform.Sdl;

public sealed class SdlException : Exception
{
    internal SdlException(string operation, string error)
        : base($"{operation} failed: {error}")
    {
    }
}
