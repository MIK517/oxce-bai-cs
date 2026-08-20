using Oxce.Mods.Files;
using Oxce.Mods.Metadata;

namespace Oxce.Mods.Discovery;

public sealed class ModCandidate
{
    public ModCandidate(ModMetadata metadata, VirtualFileLayer layer)
        : this(metadata, [layer])
    {
    }

    public ModCandidate(ModMetadata metadata, IEnumerable<VirtualFileLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(layers);
        var materialized = layers.ToArray();
        if (materialized.Length == 0 || materialized.Any(layer => layer is null))
        {
            throw new ArgumentException("A mod candidate requires at least one non-null file layer.", nameof(layers));
        }

        Metadata = metadata;
        Layers = Array.AsReadOnly(materialized);
    }

    public ModMetadata Metadata { get; }

    public IReadOnlyList<VirtualFileLayer> Layers { get; }

    public VirtualFileLayer Layer => Layers[^1];
}
