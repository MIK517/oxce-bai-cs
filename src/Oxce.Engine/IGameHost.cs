namespace Oxce.Engine;

/// <summary>Owns the application loop without exposing a specific windowing backend.</summary>
public interface IGameHost
{
    int Run(CancellationToken cancellationToken = default);
}
