using Oxce.Mods.Files;

namespace Oxce.Mods.Discovery;

public sealed class ModDiscoveryResult
{
    private readonly Lazy<IReadOnlyList<VirtualFileLayer>> _commonLayers;

    internal ModDiscoveryResult(
        IEnumerable<ModCandidate> mods,
        int rejectedCount,
        Func<IReadOnlyList<VirtualFileLayer>> mapCommonLayers)
    {
        Mods = Array.AsReadOnly(mods.ToArray());
        RejectedCount = rejectedCount;
        _commonLayers = new Lazy<IReadOnlyList<VirtualFileLayer>>(mapCommonLayers);
    }

    public IReadOnlyList<ModCandidate> Mods { get; }

    public int RejectedCount { get; }

    /// <summary>
    /// The installation-wide <c>common</c> layers, mapped from the external resource roots on
    /// first use. The reference maps them once below every mod, so they belong to the
    /// installation rather than to a single mod (<c>FileMap::setup</c>, <c>VFS::map_common</c>).
    /// </summary>
    public IReadOnlyList<VirtualFileLayer> CommonLayers => _commonLayers.Value;
}
