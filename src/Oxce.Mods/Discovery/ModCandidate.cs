using Oxce.Mods.Files;
using Oxce.Mods.Metadata;

namespace Oxce.Mods.Discovery;

public sealed record ModCandidate(ModMetadata Metadata, VirtualFileLayer Layer);
