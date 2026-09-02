using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using Oxce.Mods.Discovery;
using Oxce.Mods.Files;
using Oxce.Mods.Loading;
using Oxce.Mods.Resources;
using Oxce.Resources;

namespace Oxce.Benchmarks;

[MemoryDiagnoser]
public class ResourceRuntimeBenchmarks : IDisposable
{
    private const int PayloadBytes = 256 * 1024;
    private string _root = null!;
    private VirtualFileCatalog _directoryFiles = null!;
    private VirtualFileCatalog _zipFiles = null!;
    private ResolvedResourceCatalog _directoryCatalog = null!;
    private ResolvedResourceCatalog _zipCatalog = null!;
    private ResourceHandle _directoryHandle;
    private ResourceHandle _zipHandle;
    private ResourceRuntime _warmDirectory = null!;
    private ResourceRuntime _warmZip = null!;

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"oxce-resource-benchmark-{Guid.NewGuid():N}");
        var directoryMod = Path.Combine(_root, "directory");
        Directory.CreateDirectory(directoryMod);
        File.WriteAllText(Path.Combine(directoryMod, "metadata.yml"),
            "id: directory\nname: directory\nversion: 1.0\nisMaster: true\n");
        File.WriteAllBytes(Path.Combine(directoryMod, "payload.bin"), new byte[PayloadBytes]);
        using (var archive = ZipFile.Open(Path.Combine(_root, "archive.zip"), ZipArchiveMode.Create))
        {
            WriteEntry(archive, "metadata.yml",
                "id: archive\nname: archive\nversion: 1.0\nisMaster: true\n");
            var payload = archive.CreateEntry("payload.bin", CompressionLevel.Fastest);
            using var output = payload.Open();
            output.Write(new byte[PayloadBytes]);
        }

        _directoryFiles = CreateFiles("directory");
        _zipFiles = CreateFiles("archive");
        _directoryCatalog = ResolvedResourceCatalog.FromPaths(_directoryFiles,
            [("payload", "payload.bin", ResourceKind.Binary, ResourceLoadPolicy.Cache)]);
        _zipCatalog = ResolvedResourceCatalog.FromPaths(_zipFiles,
            [("payload", "payload.bin", ResourceKind.Binary, ResourceLoadPolicy.Cache)]);
        _directoryHandle = _directoryCatalog.GetRequired("payload");
        _zipHandle = _zipCatalog.GetRequired("payload");
        _warmDirectory = new ResourceRuntime(_directoryFiles, _directoryCatalog);
        _warmZip = new ResourceRuntime(_zipFiles, _zipCatalog);
        _ = _warmDirectory.LoadBytes(_directoryHandle);
        _ = _warmZip.LoadBytes(_zipHandle);
    }

    [Benchmark(Baseline = true)]
    public int DirectoryColdLoad()
    {
        using var runtime = new ResourceRuntime(_directoryFiles, _directoryCatalog);
        return runtime.LoadBytes(_directoryHandle).Length;
    }

    [Benchmark]
    public int DirectoryWarmCacheHit() => _warmDirectory.LoadBytes(_directoryHandle).Length;

    [Benchmark]
    public int ZipColdLoad()
    {
        using var runtime = new ResourceRuntime(_zipFiles, _zipCatalog);
        return runtime.LoadBytes(_zipHandle).Length;
    }

    [Benchmark]
    public int ZipWarmCacheHit() => _warmZip.LoadBytes(_zipHandle).Length;

    [GlobalCleanup]
    public void Cleanup()
    {
        _warmDirectory.Dispose();
        _warmZip.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    public void Dispose()
    {
        _warmDirectory?.Dispose();
        _warmZip?.Dispose();
        GC.SuppressFinalize(this);
    }

    private VirtualFileCatalog CreateFiles(string masterId)
    {
        var discovery = ModDiscovery.ScanDirectory(_root);
        var plan = ModLoadPlanner.Create(
            ModCatalog.Create(discovery.Mods),
            [new ModActivation(masterId, true)],
            masterId,
            new ModEngineIdentity("Extended", "8.6.1.0"));
        return plan.CreateVirtualFileCatalog();
    }

    private static void WriteEntry(ZipArchive archive, string name, string contents)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(contents);
    }
}
