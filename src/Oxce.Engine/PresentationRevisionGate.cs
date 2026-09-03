namespace Oxce.Engine;

/// <summary>
/// Tracks the last frame revision accepted by a presenter so unchanged simulation
/// ticks do not repeat conversion and upload work.
/// </summary>
public sealed class PresentationRevisionGate
{
    private long _revision;
    private bool _hasRevision;

    public bool TryAccept(long revision)
    {
        if (_hasRevision && revision == _revision) return false;
        _revision = revision;
        _hasRevision = true;
        return true;
    }

    public void Reset()
    {
        _revision = 0;
        _hasRevision = false;
    }
}
