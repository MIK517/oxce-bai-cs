using System.Buffers.Binary;
using Oxce.Core.Diagnostics;
using Oxce.Mods.Files;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Phase3;
using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Resources;

public static class ResourceDescriptorResolver
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".gif", ".bmp", ".lbm", ".iff", ".pcx", ".tga", ".tif", ".tiff",
    };

    private static readonly string[] MusicExtensions = [".flac", ".ogg", ".mp3", ".mod", ".wav", ".mid"];

    public static ResourceResolutionResult Resolve(
        ModLoadPlan plan,
        Phase3ContentCatalog content,
        IDiagnosticSink? diagnostics = null,
        ResourceResolutionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(content);
        diagnostics ??= NullDiagnosticSink.Instance;
        options ??= new ResourceResolutionOptions();
        options.Validate();
        var files = plan.CreateVirtualFileCatalog();
        var generation = ContentGenerationId.Next();
        var descriptors = new List<ResolvedResourceDescriptor>();
        var descriptorIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var indexedSlots = new Dictionary<ResourceSlot, int>();
        var resourceIndexes = new Dictionary<DeclaredResourceSlot, ResolvedResourceIndex>();
        var issues = new List<ResolvedResourceIssue>();
        var allocations = CreateAllocations(plan);
        var sharedSprites = new Dictionary<string, int>(options.SharedSpriteCounts, StringComparer.Ordinal);
        var sharedSounds = new Dictionary<string, int>(options.SharedSoundCounts, StringComparer.Ordinal);
        AddKnownSharedCounts(files, content.Presentation, sharedSprites, sharedSounds);

        foreach (var pair in content.Presentation.Special.Sprites.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var declaration in pair.Value)
            {
                if (!ValidateSprite(declaration))
                {
                    continue;
                }
                var allocation = Allocation(declaration.Type == "TEXTURE.DAT"
                    ? plan.Groups[0].Mod.Metadata.Id
                    : declaration.Source.ModId, declaration.Source, declaration.Type);
                var shared = sharedSprites.GetValueOrDefault(declaration.Type);
                foreach (var file in declaration.Files.OrderBy(static item => item.Key))
                {
                    var index = file.Key;
                    if (file.Value.EndsWith('/'))
                    {
                        foreach (var name in files.List(file.Value)
                                     .Where(name => ImageExtensions.Contains(Path.GetExtension(name)))
                                     .Order(StringComparer.Ordinal))
                        {
                            var declaredIndex = index++;
                            var resolvedIndex = ResolveIndex(declaredIndex, shared, allocation, "extraSprites",
                                declaration.Type, declaration.Source);
                            if (resolvedIndex is not null)
                            {
                                var handle = AddFile(ResourceKind.Sprite, $"sprite/{declaration.Type}/{resolvedIndex}",
                                    file.Value + name, ResourceLoadPolicy.Cache, resolvedIndex,
                                    declaration.Width, declaration.Height, "extraSprites", declaration.Type, declaration.Source);
                                Index(ResourceKind.Sprite, declaration.Type, declaration.Source.ModId,
                                    declaredIndex, resolvedIndex.Value, handle);
                            }
                        }
                    }
                    else
                    {
                        var resolvedIndex = ResolveIndex(index, shared, allocation, "extraSprites",
                            declaration.Type, declaration.Source);
                        if (resolvedIndex is not null)
                        {
                            var handle = AddFile(ResourceKind.Sprite, $"sprite/{declaration.Type}/{resolvedIndex}",
                                file.Value, ResourceLoadPolicy.Cache, resolvedIndex,
                                declaration.Width, declaration.Height, "extraSprites", declaration.Type, declaration.Source);
                            Index(ResourceKind.Sprite, declaration.Type, declaration.Source.ModId,
                                index, resolvedIndex.Value, handle);
                        }
                    }
                }
            }
        }

        foreach (var declaration in content.Presentation.Special.Sounds)
        {
            var allocation = Allocation(declaration.Source.ModId, declaration.Source, declaration.Type);
            var shared = sharedSounds.GetValueOrDefault(declaration.Type);
            foreach (var file in declaration.Files.OrderBy(static item => item.Key))
            {
                var index = file.Key;
                if (file.Value.EndsWith('/'))
                {
                    foreach (var name in files.List(file.Value).Order(StringComparer.Ordinal))
                    {
                        var declaredIndex = index++;
                        var resolvedIndex = ResolveIndex(declaredIndex, shared, allocation, "extraSounds",
                            declaration.Type, declaration.Source);
                        if (resolvedIndex is not null)
                        {
                            var handle = AddFile(ResourceKind.Sound, $"sound/{declaration.Type}/{resolvedIndex}",
                                file.Value + name, ResourceLoadPolicy.Cache, resolvedIndex,
                                0, 0, "extraSounds", declaration.Type, declaration.Source);
                            Index(ResourceKind.Sound, declaration.Type, declaration.Source.ModId,
                                declaredIndex, resolvedIndex.Value, handle);
                        }
                    }
                }
                else
                {
                    var resolvedIndex = ResolveIndex(index, shared, allocation, "extraSounds",
                        declaration.Type, declaration.Source);
                    if (resolvedIndex is not null)
                    {
                        var handle = AddFile(ResourceKind.Sound, $"sound/{declaration.Type}/{resolvedIndex}",
                            file.Value, ResourceLoadPolicy.Cache, resolvedIndex,
                            0, 0, "extraSounds", declaration.Type, declaration.Source);
                        Index(ResourceKind.Sound, declaration.Type, declaration.Source.ModId,
                            index, resolvedIndex.Value, handle);
                    }
                }
            }
        }

        foreach (var rule in content.Presentation.SoundDefinitions.Rules)
        {
            if (rule.Value.File.Length != 0)
            {
                AddCandidate(ResourceKind.Sound, $"sound-def/{rule.Id}", [rule.Value.File, "SOUND/" + rule.Value.File],
                    ResourceLoadPolicy.Cache, null, 0, 0, "soundDefs", rule.Id, rule.LastUpdateSource);
            }
        }
        foreach (var rule in content.Presentation.Interfaces.Rules)
        {
            AddInterfaceImage(rule.Id, "background", rule.Value.BackgroundImage, rule.LastUpdateSource);
            AddInterfaceImage(rule.Id, "alternate-background", rule.Value.AlternateBackgroundImage,
                rule.LastUpdateSource);
            for (var index = 0; index < rule.Value.UpgradedBackgroundImages.Count; index++)
            {
                AddInterfaceImage(rule.Id, $"upgraded-background/{index}",
                    rule.Value.UpgradedBackgroundImages[index].Value, rule.LastUpdateSource);
            }
        }
        foreach (var rule in content.Presentation.CustomPalettes.Rules)
        {
            if (rule.Value.File.Length != 0)
            {
                AddFile(ResourceKind.Palette, $"palette/{rule.Id}", rule.Value.File, ResourceLoadPolicy.Cache,
                null, 0, 0, "customPalettes", rule.Id, rule.LastUpdateSource);
            }
        }
        foreach (var fontFile in files.List("Language")
                     .Where(name => name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.Ordinal))
        {
            AddDirect(ResourceKind.Font, $"font/{fontFile}", "Language/" + fontFile,
                ResourceLoadPolicy.Preload, "fonts", fontFile);
        }
        foreach (var rule in content.Presentation.Music.Rules)
        {
            var name = rule.Value.ResolveName(rule.Id);
            var candidates = MusicExtensions
                .Select(extension => "SOUND/" + name + extension)
                .ToArray();
            AddOptionalCandidate(ResourceKind.Music, $"music/{rule.Id}", candidates, ResourceLoadPolicy.Stream,
                "musics", rule.Id, rule.LastUpdateSource);
        }
        foreach (var rule in content.Presentation.Videos.Rules)
        {
            for (var index = 0; index < rule.Value.Videos.Count; index++)
            {
                AddOptionalCandidate(ResourceKind.Video, $"video/{rule.Id}/{index}",
                    Candidates(rule.Value.Videos[index], "VIDEO"), ResourceLoadPolicy.Stream,
                    "cutscenes", rule.Id, rule.LastUpdateSource);
            }
            for (var index = 0; index < rule.Value.AudioTracks.Count; index++)
            {
                AddOptionalCandidate(ResourceKind.Music, $"video-audio/{rule.Id}/{index}",
                    Candidates(rule.Value.AudioTracks[index], "SOUND"), ResourceLoadPolicy.Stream,
                    "cutscenes", rule.Id, rule.LastUpdateSource);
            }
            for (var index = 0; index < rule.Value.Slides.Count; index++)
            {
                AddFile(ResourceKind.IndexedImage, $"slide/{rule.Id}/{index}", rule.Value.Slides[index].ImagePath,
                    ResourceLoadPolicy.Cache, null, rule.Value.Slides[index].Width, rule.Value.Slides[index].Height,
                    "cutscenes", rule.Id, rule.LastUpdateSource);
            }
        }
        foreach (var rule in content.TerrainDeployment.Terrains.Rules)
        {
            foreach (var dataSet in rule.Value.MapDataSets.Distinct(StringComparer.Ordinal))
            {
                AddFile(ResourceKind.Terrain, $"terrain/{rule.Id}/dataset/{dataSet}/mcd",
                    $"TERRAIN/{dataSet}.MCD", ResourceLoadPolicy.Cache, null, 0, 0,
                    "terrains", rule.Id, rule.LastUpdateSource);
                AddFile(ResourceKind.Sprite, $"terrain/{rule.Id}/dataset/{dataSet}/pck",
                    $"TERRAIN/{dataSet}.PCK", ResourceLoadPolicy.Cache, null, 32, 40,
                    "terrains", rule.Id, rule.LastUpdateSource);
                AddFile(ResourceKind.Binary, $"terrain/{rule.Id}/dataset/{dataSet}/tab",
                    $"TERRAIN/{dataSet}.TAB", ResourceLoadPolicy.Cache, null, 0, 0,
                    "terrains", rule.Id, rule.LastUpdateSource);
            }
            foreach (var block in rule.Value.MapBlocks)
            {
                AddOptionalCandidate(ResourceKind.Terrain, $"terrain/{rule.Id}/map/{block.Name}",
                    [$"MAPS/{block.Name}.MAP"], ResourceLoadPolicy.Cache,
                    "terrains", rule.Id, rule.LastUpdateSource, block.Width, block.Length);
                AddOptionalCandidate(ResourceKind.Terrain, $"terrain/{rule.Id}/route/{block.Name}",
                    [$"ROUTES/{block.Name}.RMP"], ResourceLoadPolicy.Cache,
                    "terrains", rule.Id, rule.LastUpdateSource);
            }
        }

        return new ResourceResolutionResult(
            new ResolvedResourceCatalog(generation, descriptors, resourceIndexes.Values),
            Array.AsReadOnly(issues.ToArray()));

        ModAllocation Allocation(string modId, RuleOperationSource source, string ownerId)
        {
            if (allocations.TryGetValue(modId, out var allocation)) return allocation;
            Report(ModDiagnosticCodes.InvalidResourceDescriptor,
                $"Resource declaration references unknown mod '{modId}'.", "resources", ownerId, string.Empty, source);
            return default;
        }

        void AddInterfaceImage(string ownerId, string role, string path, RuleOperationSource source)
        {
            if (path.Length == 0) return;
            var declared = descriptors.LastOrDefault(descriptor => descriptor.Kind == ResourceKind.Sprite &&
                string.Equals(descriptor.OwnerId, path, StringComparison.Ordinal));
            if (declared is not null)
            {
                Add(ResourceKind.IndexedImage, $"interface/{ownerId}/{role}",
                    files.GetRequired(declared.CanonicalPath), ResourceLoadPolicy.Cache, declared.RuntimeIndex,
                    declared.Width, declared.Height, "interfaces", ownerId);
                return;
            }
            AddCandidate(ResourceKind.IndexedImage, $"interface/{ownerId}/{role}",
                [path, "GEOGRAPH/" + path, "UFOGRAPH/" + path], ResourceLoadPolicy.Cache,
                null, 0, 0, "interfaces", ownerId, source);
        }

        bool ValidateSprite(ExtraSpriteDeclaration declaration)
        {
            if (declaration.Width <= 0 || declaration.Height <= 0 ||
                declaration.Width > options.MaximumDimension || declaration.Height > options.MaximumDimension ||
                (declaration.SubX == 0) != (declaration.SubY == 0) || declaration.SubX < 0 || declaration.SubY < 0 ||
                declaration.SubX > options.MaximumDimension || declaration.SubY > options.MaximumDimension)
            {
                Report(ModDiagnosticCodes.InvalidResourceDescriptor,
                    $"Extra sprite '{declaration.Type}' has invalid dimensions or subdivision.",
                    "extraSprites", declaration.Type, string.Empty, declaration.Source);
                return false;
            }
            return true;
        }

        int? ResolveIndex(int index, int shared, ModAllocation allocation, string section, string ownerId,
            RuleOperationSource source)
        {
            if (index < 0 || index >= allocation.Size)
            {
                Report(ModDiagnosticCodes.InvalidResourceDescriptor,
                    $"Resource index {index} is outside mod '{source.ModId}' reserved range 0..{allocation.Size - 1}.",
                    section, ownerId, string.Empty, source);
                return null;
            }
            return index >= shared ? checked(index + allocation.Offset) : index;
        }

        void AddOptionalCandidate(ResourceKind kind, string id, IReadOnlyList<string> candidates,
            ResourceLoadPolicy policy, string section, string ownerId, RuleOperationSource source,
            int width = 0, int height = 0)
        {
            foreach (var candidate in candidates)
            {
                if (files.TryGet(candidate, out var entry))
                {
                    Add(kind, id, entry!, policy, null, width, height, section, ownerId);
                    return;
                }
            }
        }

        void AddCandidate(ResourceKind kind, string id, IReadOnlyList<string> candidates, ResourceLoadPolicy policy,
            int? runtimeIndex, int width, int height, string section, string ownerId, RuleOperationSource source)
        {
            foreach (var candidate in candidates)
            {
                if (files.TryGet(candidate, out var entry))
                {
                    Add(kind, id, entry!, policy, runtimeIndex, width, height, section, ownerId);
                    return;
                }
            }
            Report(ModDiagnosticCodes.MissingDeclaredResource,
                $"Rule '{ownerId}' in section '{section}' declares missing resource '{candidates[0]}'.",
                section, ownerId, candidates[0], source);
        }

        ResourceHandle? AddFile(ResourceKind kind, string id, string path, ResourceLoadPolicy policy, int? runtimeIndex,
            int width, int height, string section, string ownerId, RuleOperationSource source)
        {
            if (!files.TryGet(path, out var entry))
            {
                Report(ModDiagnosticCodes.MissingDeclaredResource,
                    $"Rule '{ownerId}' in section '{section}' declares missing resource '{path}'.",
                    section, ownerId, path, source);
                return null;
            }
            return Add(kind, id, entry!, policy, runtimeIndex, width, height, section, ownerId);
        }

        void AddDirect(ResourceKind kind, string id, string path, ResourceLoadPolicy policy,
            string section, string ownerId)
        {
            if (files.TryGet(path, out var entry))
            {
                Add(kind, id, entry!, policy, null, 0, 0, section, ownerId);
            }
        }

        ResourceHandle Add(ResourceKind kind, string id, VirtualFileEntry entry, ResourceLoadPolicy policy, int? runtimeIndex,
            int width, int height, string section, string ownerId)
        {
            if (runtimeIndex is not null)
            {
                var slot = new ResourceSlot(kind, section, ownerId, runtimeIndex.Value);
                if (indexedSlots.TryGetValue(slot, out var existingIndex))
                {
                    var existing = descriptors[existingIndex];
                    descriptors[existingIndex] = new ResolvedResourceDescriptor(existing.Handle, existing.Id, kind,
                        entry.CanonicalPath, entry.SourcePath, entry.Provenance, policy, runtimeIndex,
                        width, height, section, ownerId);
                    return existing.Handle;
                }
            }
            if (descriptors.Count >= options.MaximumDescriptors)
            {
                throw new InvalidDataException(
                    $"{ModDiagnosticCodes.ResourceLimitExceeded}: Resolved resource descriptors exceed the {options.MaximumDescriptors}-entry limit.");
            }
            var uniqueId = id;
            for (var suffix = 2; descriptorIds.ContainsKey(uniqueId); suffix++)
            {
                uniqueId = id + "/" + suffix;
            }
            var handle = new ResourceHandle(generation, descriptors.Count, kind);
            descriptors.Add(new ResolvedResourceDescriptor(handle, uniqueId, kind, entry.CanonicalPath,
                entry.SourcePath, entry.Provenance, policy, runtimeIndex, width, height, section, ownerId));
            descriptorIds.Add(uniqueId, handle.Index);
            if (runtimeIndex is not null)
            {
                indexedSlots.Add(new ResourceSlot(kind, section, ownerId, runtimeIndex.Value), handle.Index);
            }
            return handle;
        }

        void Index(
            ResourceKind kind,
            string setId,
            string modId,
            int declaredIndex,
            int runtimeIndex,
            ResourceHandle? handle)
        {
            if (!handle.HasValue) return;
            var slot = new DeclaredResourceSlot(kind, setId, modId, declaredIndex);
            resourceIndexes[slot] = new ResolvedResourceIndex(
                kind, setId, modId, declaredIndex, runtimeIndex, handle.Value);
        }

        void Report(string code, string message, string section, string ownerId, string path, RuleOperationSource source)
        {
            var issue = new ResolvedResourceIssue(code, message, section, ownerId, path, source);
            issues.Add(issue);
            diagnostics.Report(new DiagnosticEvent(code, DiagnosticSeverity.Error, message, source.Span,
                new DiagnosticContext(source.LayerId, source.ModId, section, ownerId)));
        }
    }

    private static IReadOnlyList<string> Candidates(string path, string defaultDirectory)
    {
        if (path.Contains('/', StringComparison.Ordinal)) return [path];
        return [path, defaultDirectory + "/" + path];
    }

    private static Dictionary<string, ModAllocation> CreateAllocations(ModLoadPlan plan)
    {
        var result = new Dictionary<string, ModAllocation>(StringComparer.Ordinal);
        var offset = 0;
        foreach (var group in plan.Groups)
        {
            var size = checked(group.Mod.Metadata.ReservedSpace * 1000);
            result.Add(group.Mod.Metadata.Id, new ModAllocation(offset, size));
            offset = checked(offset + size);
        }
        return result;
    }

    private static void AddKnownSharedCounts(
        VirtualFileCatalog files,
        PresentationRuleCatalog presentation,
        Dictionary<string, int> sprites,
        Dictionary<string, int> sounds)
    {
        foreach (var pair in new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BIGOBS.PCK"] = "UNITS/BIGOBS.TAB",
            ["FLOOROB.PCK"] = "UNITS/FLOOROB.TAB",
            ["HANDOB.PCK"] = "UNITS/HANDOB.TAB",
            ["SMOKE.PCK"] = "UFOGRAPH/SMOKE.TAB",
            ["HIT.PCK"] = "UFOGRAPH/HIT.TAB",
            ["BASEBITS.PCK"] = "GEOGRAPH/BASEBITS.TAB",
            ["INTICON.PCK"] = "GEOGRAPH/INTICON.TAB",
        })
        {
            if (!sprites.ContainsKey(pair.Key) && files.TryGet(pair.Value, out var entry))
            {
                sprites[pair.Key] = ReadTabCount(entry!);
            }
        }
        sprites.TryAdd("CustomArmorPreviews", 0);
        sprites.TryAdd("CustomItemPreviews", 0);
        sprites.TryAdd("Projectiles", 385);
        sprites.TryAdd("UnderwaterProjectiles", 385);
        sprites.TryAdd("GlobeMarkers", 9);
        sprites.TryAdd("TinyRanks", 6);
        sprites.TryAdd("Touch", 10);
        if (sprites.TryGetValue("SMOKE.PCK", out var smoke)) sprites.TryAdd("X1.PCK", smoke);

        foreach (var rule in presentation.ResourceConfigSoundDefinitions.Rules)
        {
            var count = rule.Value.SoundRanges.Sum(static range => checked(range.Last - range.First + 1)) +
                rule.Value.Sounds.Count;
            sounds.TryAdd(rule.Id, count);
        }
        if (presentation.ResourceConfigSoundDefinitions.Rules.Count == 0)
        {
            AddSound("GEO.CAT", ["SOUND/SAMPLE.CAT", "SOUND/SOUND2.CAT"]);
            AddSound("BATTLE.CAT", ["SOUND/SAMPLE2.CAT", "SOUND/SOUND1.CAT"]);
        }
        if (sounds.TryGetValue("BATTLE.CAT", out var battle)) sounds.TryAdd("BATTLE2.CAT", battle);

        void AddSound(string name, IReadOnlyList<string> paths)
        {
            if (sounds.ContainsKey(name)) return;
            foreach (var path in paths)
            {
                if (!files.TryGet(path, out var entry)) continue;
                sounds[name] = ReadCatCount(entry!);
                return;
            }
            sounds[name] = 0;
        }
    }

    private static int ReadTabCount(VirtualFileEntry entry)
    {
        using var stream = entry.OpenRead();
        if (stream.Length == 0) return 0;
        if (stream.Length < sizeof(uint)) return checked((int)(stream.Length / sizeof(ushort)));
        Span<byte> first = stackalloc byte[sizeof(uint)];
        stream.ReadExactly(first);
        var width = BinaryPrimitives.ReadUInt32LittleEndian(first) == 0 ? sizeof(uint) : sizeof(ushort);
        if (stream.Length % width != 0) throw new InvalidDataException($"TAB resource '{entry.SourcePath}' is malformed.");
        return checked((int)(stream.Length / width));
    }

    private static int ReadCatCount(VirtualFileEntry entry)
    {
        using var stream = entry.OpenRead();
        Span<byte> first = stackalloc byte[sizeof(uint)];
        stream.ReadExactly(first);
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(first);
        if (offset % 8 != 0 || offset > stream.Length) throw new InvalidDataException($"CAT resource '{entry.SourcePath}' is malformed.");
        return checked((int)(offset / 8));
    }

    private readonly record struct ModAllocation(int Offset, int Size);
    private readonly record struct ResourceSlot(ResourceKind Kind, string Section, string OwnerId, int RuntimeIndex);
    private readonly record struct DeclaredResourceSlot(
        ResourceKind Kind,
        string SetId,
        string ModId,
        int DeclaredIndex);
}
