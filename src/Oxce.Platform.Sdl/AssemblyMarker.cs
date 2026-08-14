using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Oxce.UnitTests")]
[assembly: DisableRuntimeMarshalling]

namespace Oxce.Platform.Sdl;

/// <summary>Marks the assembly containing SDL3 window, input, audio, and presentation code.</summary>
public sealed class AssemblyMarker
{
    private AssemblyMarker() { }
}
