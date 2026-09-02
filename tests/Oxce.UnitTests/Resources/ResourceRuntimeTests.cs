using System.Text;
using System.IO.Compression;
using Oxce.Mods.Discovery;
using Oxce.Mods.Files;
using Oxce.Mods.Loading;
using Oxce.Mods.Resources;
using Oxce.Resources;
using Xunit;

namespace Oxce.UnitTests.Resources;

public sealed class ResourceRuntimeTests
{
    [Fact]
    public void CacheReportsHitsAndEvictsLeastRecentlyUsedEntriesWithinItsByteBudget()
    {
        using var fixture = new ResourceFixture(("a.bin", "aaaa"), ("b.bin", "bbbb"), ("c.bin", "cccc"));
        var catalog = fixture.CreateResolvedCatalog(ResourceLoadPolicy.Cache, "a.bin", "b.bin", "c.bin");
        using var runtime = new ResourceRuntime(fixture.Files, catalog,
            new ResourceCacheOptions { MaximumBytes = 8, MaximumEntryBytes = 8 });
        var decodes = 0;

        string Decode(ResourceHandle handle) => runtime.Load(handle, "text", stream =>
        {
            decodes++;
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var value = reader.ReadToEnd();
            return new ResourceDecodeResult<string>(value, Encoding.UTF8.GetByteCount(value));
        });

        Assert.Equal("aaaa", Decode(catalog.GetRequired("a.bin")));
        Assert.Equal("bbbb", Decode(catalog.GetRequired("b.bin")));
        Assert.Equal("aaaa", Decode(catalog.GetRequired("a.bin")));
        Assert.Equal("cccc", Decode(catalog.GetRequired("c.bin")));
        Assert.Equal("bbbb", Decode(catalog.GetRequired("b.bin")));

        Assert.Equal(4, decodes);
        Assert.Equal(new ResourceCacheTelemetry(1, 4, 4, 2, 0, 8, 2), runtime.Telemetry);
        runtime.InvalidateGeneration(catalog.Generation);
        Assert.Equal(new ResourceCacheTelemetry(1, 4, 4, 2, 0, 0, 0), runtime.Telemetry);
    }

    [Fact]
    public void RejectsOversizedDecodedEntriesAndStaleGenerationHandles()
    {
        using var fixture = new ResourceFixture(("large.bin", "0123456789"));
        var catalog = fixture.CreateResolvedCatalog(ResourceLoadPolicy.Cache, "large.bin");
        using var runtime = new ResourceRuntime(fixture.Files, catalog,
            new ResourceCacheOptions { MaximumBytes = 8, MaximumEntryBytes = 8 });

        Assert.Throws<InvalidDataException>(() => runtime.Load(
            catalog.GetRequired("large.bin"),
            "oversized",
            _ => new ResourceDecodeResult<byte[]>(new byte[9], 9)));

        var replacement = fixture.CreateResolvedCatalog(ResourceLoadPolicy.Cache, "large.bin");
        Assert.Throws<InvalidOperationException>(() => runtime.LoadBytes(replacement.GetRequired("large.bin")));
        Assert.Equal(1, runtime.Telemetry.RejectedOversizedEntries);
    }

    [Fact]
    public void StreamingBypassesCacheWhilePreloadWarmsDeclaredGroups()
    {
        using var fixture = new ResourceFixture(("cached.bin", "cache"), ("stream.bin", "stream"));
        var catalog = ResolvedResourceCatalog.FromPaths(fixture.Files,
        [
            ("cached", "cached.bin", ResourceKind.Binary, ResourceLoadPolicy.Preload),
            ("stream", "stream.bin", ResourceKind.Music, ResourceLoadPolicy.Stream),
        ]);
        using var runtime = new ResourceRuntime(fixture.Files, catalog,
            new ResourceCacheOptions { MaximumBytes = 32, MaximumEntryBytes = 16 });

        runtime.Preload(new ResourcePreloadGroup("startup", [catalog.GetRequired("cached")]));
        Assert.Equal("cache", Encoding.UTF8.GetString(runtime.LoadBytes(catalog.GetRequired("cached")).Span));
        using var stream = runtime.OpenStream(catalog.GetRequired("stream"));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Assert.Equal("stream", reader.ReadToEnd());
        Assert.Throws<InvalidOperationException>(() => runtime.LoadBytes(catalog.GetRequired("stream")));

        Assert.Equal(new ResourceCacheTelemetry(1, 1, 1, 0, 0, 5, 1), runtime.Telemetry);
    }

    [Fact]
    public void ArchiveBackedEntriesUseTheSameLazyCachePath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oxce-resource-zip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using (var archive = ZipFile.Open(Path.Combine(root, "fixture.zip"), ZipArchiveMode.Create))
            {
                WriteEntry(archive, "metadata.yml", "id: fixture\nname: fixture\nisMaster: true\n");
                WriteEntry(archive, "payload.bin", "archive-payload");
            }
            var discovery = ModDiscovery.ScanDirectory(root);
            var plan = ModLoadPlanner.Create(
                ModCatalog.Create(discovery.Mods),
                [new ModActivation("fixture", true)],
                "fixture",
                new ModEngineIdentity("Extended", "8.6.1.0"));
            var files = plan.CreateVirtualFileCatalog();
            var catalog = ResolvedResourceCatalog.FromPaths(files,
                [("payload", "payload.bin", ResourceKind.Binary, ResourceLoadPolicy.Cache)]);
            using var runtime = new ResourceRuntime(files, catalog);

            Assert.Equal("archive-payload", Encoding.UTF8.GetString(runtime.LoadBytes(catalog.GetRequired("payload")).Span));
            Assert.Equal("archive-payload", Encoding.UTF8.GetString(runtime.LoadBytes(catalog.GetRequired("payload")).Span));
            Assert.Equal((1, 1, 1), (runtime.Telemetry.Hits, runtime.Telemetry.Misses, runtime.Telemetry.Loads));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string contents)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }

    private sealed class ResourceFixture : IDisposable
    {
        public ResourceFixture(params (string Name, string Contents)[] files)
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-resources-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            foreach (var file in files)
            {
                File.WriteAllText(Path.Combine(Root, file.Name), file.Contents, new UTF8Encoding(false));
            }
            Files = new VirtualFileCatalog(
                [VirtualFileLayer.ScanDirectory(Root, "fixture", options: new DirectoryScanOptions { IgnoreRulesets = true })]);
        }

        public string Root { get; }
        public VirtualFileCatalog Files { get; }

        public ResolvedResourceCatalog CreateResolvedCatalog(ResourceLoadPolicy policy, params string[] paths) =>
            ResolvedResourceCatalog.FromPaths(Files,
                paths.Select(path => (path, path, ResourceKind.Binary, policy)));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
