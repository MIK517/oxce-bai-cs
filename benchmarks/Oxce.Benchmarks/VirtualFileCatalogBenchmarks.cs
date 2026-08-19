using BenchmarkDotNet.Attributes;
using Oxce.Mods.Files;

namespace Oxce.Benchmarks;

[MemoryDiagnoser]
public class VirtualFileCatalogBenchmarks
{
    private const int ScanFileCount = 5_000;
    private const int CatalogLayerCount = 8;
    private const int FilesPerCatalogLayer = 2_000;

    private string _scanRoot = null!;
    private VirtualFileLayer[] _catalogLayers = null!;

    [GlobalSetup]
    public void Setup()
    {
        _scanRoot = Path.Combine(Path.GetTempPath(), $"oxce-vfs-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scanRoot);
        for (var index = 0; index < ScanFileCount; ++index)
        {
            var directory = Path.Combine(_scanRoot, $"group-{index % 50:D2}");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"resource-{index:D5}.dat"), [(byte)index]);
        }

        _catalogLayers = Enumerable.Range(0, CatalogLayerCount)
            .Select(CreateLayer)
            .ToArray();
    }

    [Benchmark]
    public VirtualFileLayer ScanLargeDirectory() =>
        VirtualFileLayer.ScanDirectory(_scanRoot, "benchmark-scan");

    [Benchmark]
    public VirtualFileCatalog ConstructLargeLayeredCatalog() => new(_catalogLayers);

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_scanRoot))
        {
            Directory.Delete(_scanRoot, recursive: true);
        }
    }

    private static VirtualFileLayer CreateLayer(int layerIndex)
    {
        var sources = Enumerable.Range(0, FilesPerCatalogLayer)
            .Select(index => new VirtualFileSource(
                $"group-{index % 50:D2}/resource-{index:D5}.dat",
                $"layer-{layerIndex}-resource-{index:D5}"));
        return VirtualFileLayer.FromEntries(
            new VirtualFileProvenance($"layer-{layerIndex}", $"mod-{layerIndex}", $"layer-{layerIndex}"),
            sources);
    }
}
