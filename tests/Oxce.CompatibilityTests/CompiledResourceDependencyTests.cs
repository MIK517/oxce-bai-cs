using System.Buffers.Binary;
using System.IO.Compression;
using Oxce.Mods.Bootstrap;
using Oxce.Mods.Loading;
using Oxce.Mods.Resources;
using Oxce.Mods.Rulesets.Content;
using Xunit;

namespace Oxce.CompatibilityTests;

// Generated redistributable TAB/CAT fixtures. The expected shared/offset decisions
// follow Mod.cpp, ExtraSprites::getFrame and ExtraSounds::loadSound at 4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15.
public sealed class CompiledResourceDependencyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SameSizeHeaderReplacementMatchesFreshBuild(bool sound, bool archive)
    {
        using var fixture = new Installation(sound, archive);
        fixture.Write(fixture.AssetPath, fixture.Header(sharedCount: 1));
        var initial = fixture.Load();
        Assert.Equal(1001, fixture.Index(initial));
        Assert.Equal(CompiledContentCacheStatus.Hit, fixture.Load().CacheStatus);

        fixture.Write(fixture.AssetPath, fixture.Header(sharedCount: 2));
        var changed = fixture.Load();
        Assert.Equal(CompiledContentCacheStatus.Rejected, changed.CacheStatus);
        Assert.Equal(1, fixture.Index(changed));
        fixture.AssertEquivalentToFresh(changed);
        fixture.AssertEquivalentToFresh(fixture.Load());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MalformedReplacementCannotReuseSuccessfulCache(bool sound, bool archive)
    {
        using var fixture = new Installation(sound, archive);
        fixture.Write(fixture.AssetPath, fixture.Header(sharedCount: 1));
        Assert.True(fixture.Load().IsSuccess);
        var malformed = sound ? new byte[16] : new byte[5];
        if (sound) BinaryPrimitives.WriteUInt32LittleEndian(malformed, 17);
        fixture.Write(fixture.AssetPath, malformed);
        var cached = fixture.Load();
        var fresh = fixture.Load(enabled: false);
        Assert.False(cached.IsSuccess);
        Assert.Null(cached.Content);
        Assert.False(fresh.IsSuccess);
        Assert.Equal(fresh.Failure, cached.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TabLengthChangeWithIdenticalPrefixUpdatesSharedRange(bool archive)
    {
        using var fixture = new Installation(sound: false, archive);
        fixture.Write(fixture.AssetPath, new byte[4]);
        Assert.Equal(1001, fixture.Index(fixture.Load()));
        fixture.Write(fixture.AssetPath, new byte[8]);
        var changed = fixture.Load();
        Assert.Equal(CompiledContentCacheStatus.Rejected, changed.CacheStatus);
        Assert.Equal(1, fixture.Index(changed));
        fixture.AssertEquivalentToFresh(changed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LengthAndTruncatedCatChangesCannotReuseCachedCounts(bool archive)
    {
        using var fixture = new Installation(sound: true, archive);
        fixture.Write(fixture.AssetPath, fixture.Header(sharedCount: 2));
        Assert.Equal(1, fixture.Index(fixture.Load()));
        // Same header, new length: offset 16 no longer fits the file.
        fixture.Write(fixture.AssetPath, fixture.Header(sharedCount: 2)[..8]);
        Assert.False(fixture.Load().IsSuccess);
        fixture.Write(fixture.AssetPath, [0, 0]);
        Assert.False(fixture.Load().IsSuccess);
        Assert.False(fixture.Load(enabled: false).IsSuccess);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LazyPayloadAndUnusedFallbackChangesKeepCacheHit(bool archive)
    {
        using var fixture = new Installation(sound: true, archive);
        var payload = fixture.Header(sharedCount: 1);
        fixture.Write(fixture.AssetPath, payload);
        fixture.Write("SOUND/SOUND2.CAT", fixture.Header(sharedCount: 1));
        Assert.Equal(1001, fixture.Index(fixture.Load()));
        payload[^1] = 42;
        fixture.Write(fixture.AssetPath, payload);
        fixture.Write("SOUND/SOUND2.CAT", fixture.Header(sharedCount: 2));
        var cached = fixture.Load();
        Assert.Equal(CompiledContentCacheStatus.Hit, cached.CacheStatus);
        fixture.AssertEquivalentToFresh(cached);
        fixture.Delete(fixture.AssetPath);
        var fallback = fixture.Load();
        Assert.Equal(CompiledContentCacheStatus.Rejected, fallback.CacheStatus);
        Assert.Equal(1, fixture.Index(fallback));
        fixture.AssertEquivalentToFresh(fallback);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyWinningLayerMetadataAffectsCache(bool archive)
    {
        using var fixture = new Installation(sound: false, archive);
        fixture.Write(fixture.AssetPath, fixture.Header(sharedCount: 1));
        fixture.Write(fixture.AssetPath, fixture.Header(sharedCount: 1), addon: true);
        Assert.Equal(1001, fixture.Index(fixture.Load()));
        fixture.Write(fixture.AssetPath, fixture.Header(sharedCount: 2));
        Assert.Equal(CompiledContentCacheStatus.Hit, fixture.Load().CacheStatus);
        fixture.Write(fixture.AssetPath, fixture.Header(sharedCount: 2), addon: true);
        var changed = fixture.Load();
        Assert.Equal(CompiledContentCacheStatus.Rejected, changed.CacheStatus);
        Assert.Equal(1, fixture.Index(changed));
        fixture.AssertEquivalentToFresh(changed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitSharedCountsOverrideAssetMetadata(bool sound)
    {
        using var fixture = new Installation(sound, archive: false);
        fixture.Write(fixture.AssetPath, fixture.Header(sharedCount: 1));
        var resolution = sound
            ? new ResourceResolutionOptions { SharedSoundCounts = new Dictionary<string, int> { ["GEO.CAT"] = 2 } }
            : new ResourceResolutionOptions { SharedSpriteCounts = new Dictionary<string, int> { ["BASEBITS.PCK"] = 2 } };
        Assert.Equal(1, fixture.Index(fixture.Load(resolution: resolution)));
        fixture.Write(fixture.AssetPath, [255]);
        var cached = fixture.Load(resolution: resolution);
        Assert.Equal(CompiledContentCacheStatus.Hit, cached.CacheStatus);
        Assert.Equal(1, fixture.Index(cached));
        Assert.Equal(1, fixture.Index(fixture.Load(enabled: false, resolution: resolution)));
    }

    private sealed class Installation : IDisposable
    {
        private readonly bool _sound;
        private readonly bool _archive;
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"oxce-resource-dependencies-{Guid.NewGuid():N}");

        public Installation(bool sound, bool archive)
        {
            _sound = sound;
            _archive = archive;
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Oxce.slnx")))
                repository = repository.Parent;
            var source = Path.Combine(repository!.FullName, "fixtures", "public", "mods", "runtime-rule-linking");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(_root, "standard", Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            Directory.CreateDirectory(Path.Combine(_root, "user", "mods"));
            var addon = Path.Combine(_root, "standard", "runtime-addon");
            var rule = Directory.EnumerateFiles(addon, "*.rul", SearchOption.AllDirectories).Single();
            File.WriteAllText(rule, File.ReadAllText(rule)
                .Replace("files: {0:", "files: {1:", StringComparison.Ordinal)
                .Replace("index: 0", "index: 1", StringComparison.Ordinal));
            if (sound)
            {
                File.AppendAllText(rule, "\nextraSounds:\n  - type: GEO.CAT\n    files: {1: Resources/test.wav}\n");
                File.WriteAllBytes(Path.Combine(addon, "Resources", "test.wav"), []);
            }
            if (archive)
            {
                foreach (var directory in Directory.GetDirectories(Path.Combine(_root, "standard")))
                {
                    ZipFile.CreateFromDirectory(directory, directory + ".zip");
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        public string AssetPath => _sound ? "SOUND/SAMPLE.CAT" : "GEOGRAPH/BASEBITS.TAB";

        public byte[] Header(int sharedCount)
        {
            var bytes = new byte[_sound ? 16 : 4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, _sound ? (uint)(sharedCount * 8) : sharedCount == 1 ? 0u : 1u);
            return bytes;
        }

        public void Write(string path, byte[] bytes, bool addon = false)
        {
            var mod = Path.Combine(_root, "standard", addon ? "runtime-addon" : "runtime-master");
            if (_archive)
            {
                using var archive = ZipFile.Open(mod + ".zip", ZipArchiveMode.Update);
                archive.GetEntry(path)?.Delete();
                using var output = archive.CreateEntry(path).Open();
                output.Write(bytes);
            }
            else
            {
                var target = Path.Combine(mod, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            }
        }

        public void Delete(string path)
        {
            var mod = Path.Combine(_root, "standard", "runtime-master");
            if (_archive)
            {
                using var archive = ZipFile.Open(mod + ".zip", ZipArchiveMode.Update);
                archive.GetEntry(path)!.Delete();
            }
            else File.Delete(Path.Combine(mod, path));
        }

        public InstallationContentLoadResult Load(bool enabled = true, ResourceResolutionOptions? resolution = null) =>
            InstallationContentLoader.Load(
                InstallationLoadRequest.ForMasterAndAddOn(_root, "runtime-master", "runtime-addon",
                    new ModEngineIdentity("Extended", "8.6.1.0")),
                new InstallationContentLoadOptions
                {
                    Cache = new CompiledContentCacheOptions { Enabled = enabled },
                    Content = new ContentSnapshotOptions { ResourceResolution = resolution ?? new() },
                },
                cancellationToken: TestContext.Current.CancellationToken);

        public int Index(InstallationContentLoadResult result)
        {
            Assert.True(result.IsSuccess, result.DescribeFailure());
            return Assert.Single(result.Content!.Resources.Indexes,
                index => index.SetId == (_sound ? "GEO.CAT" : "BASEBITS.PCK")).RuntimeIndex;
        }

        public void AssertEquivalentToFresh(InstallationContentLoadResult result)
        {
            var fresh = Load(enabled: false);
            Assert.Equal(Index(fresh), Index(result));
            Assert.Equal(fresh.Content!.Resources.Indexes.Select(IndexValue), result.Content!.Resources.Indexes.Select(IndexValue));
            Assert.Equal(fresh.Content.Resources.Descriptors.Select(DescriptorValue), result.Content.Resources.Descriptors.Select(DescriptorValue));
            Assert.Equal(fresh.Diagnostics, result.Diagnostics);

            static object IndexValue(ResolvedResourceIndex index) =>
                new { index.Kind, index.SetId, index.ModId, index.DeclaredIndex, index.RuntimeIndex, index.Handle.Index };
            static object DescriptorValue(ResolvedResourceDescriptor descriptor) =>
                new
                {
                    descriptor.Id,
                    descriptor.Kind,
                    descriptor.CanonicalPath,
                    descriptor.SourcePath,
                    descriptor.Provenance,
                    descriptor.RuntimeIndex,
                    descriptor.Width,
                    descriptor.Height,
                    descriptor.LoadPolicy,
                    descriptor.OwnerSection,
                    descriptor.OwnerId
                };
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
