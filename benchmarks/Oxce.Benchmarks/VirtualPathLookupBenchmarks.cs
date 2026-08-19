using BenchmarkDotNet.Attributes;
using Oxce.Mods.Files;

namespace Oxce.Benchmarks;

[MemoryDiagnoser]
public class VirtualPathLookupBenchmarks
{
    private const int Operations = 1_024;
    private string[] _pathsToNormalize = null!;
    private string[] _pathsToFind = null!;
    private VirtualFileCatalog _catalog = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pathsToNormalize = Enumerable.Range(0, Operations)
            .Select(index => $"Ruleset\\Group-{index % 32:D2}\\Resource-{index:D4}.DAT")
            .ToArray();
        _pathsToFind = Enumerable.Range(0, Operations)
            .Select(index => index % 4 == 0
                ? $"missing/group-{index % 32:D2}/resource-{index:D4}.dat"
                : $"GROUP-{index % 32:D2}/RESOURCE-{index % 512:D4}.DAT")
            .ToArray();
        var layers = Enumerable.Range(0, 8)
            .Select(layerIndex => VirtualFileLayer.FromEntries(
                new VirtualFileProvenance($"layer-{layerIndex}", $"mod-{layerIndex}", $"layer-{layerIndex}"),
                Enumerable.Range(0, 512).Select(index => new VirtualFileSource(
                    $"group-{index % 32:D2}/resource-{index:D4}.dat",
                    $"layer-{layerIndex}-resource-{index:D4}"))))
            .ToArray();
        _catalog = new VirtualFileCatalog(layers);
    }

    [Benchmark(OperationsPerInvoke = Operations)]
    public int NormalizePaths()
    {
        var length = 0;
        foreach (var path in _pathsToNormalize)
        {
            length += VirtualPath.NormalizeFile(path).Length;
        }

        return length;
    }

    [Benchmark(OperationsPerInvoke = Operations)]
    public int LayeredLookup()
    {
        var found = 0;
        foreach (var path in _pathsToFind)
        {
            found += _catalog.TryGet(path, out _) ? 1 : 0;
        }

        return found;
    }
}
