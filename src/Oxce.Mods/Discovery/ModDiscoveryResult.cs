namespace Oxce.Mods.Discovery;

public sealed class ModDiscoveryResult
{
    internal ModDiscoveryResult(IEnumerable<ModCandidate> mods, int rejectedCount)
    {
        Mods = Array.AsReadOnly(mods.ToArray());
        RejectedCount = rejectedCount;
    }

    public IReadOnlyList<ModCandidate> Mods { get; }

    public int RejectedCount { get; }
}
