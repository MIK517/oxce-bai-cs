using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Oxce.UnitTests")]

namespace Oxce.Gameplay;

/// <summary>Marks the assembly containing geoscape, bases, and battlescape rules.</summary>
public sealed class AssemblyMarker
{
    private AssemblyMarker() { }
}
