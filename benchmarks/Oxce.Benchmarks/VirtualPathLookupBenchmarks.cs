using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Oxce.Mods.Files;

namespace Oxce.Benchmarks;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class VirtualPathLookupBenchmarks
{
    private const int Operations = 1_024;
    private string[] _pathsToNormalize = null!;
    private string[] _unicodePathsToNormalize = null!;
    private string[] _pathsToFind = null!;
    private VirtualFileLayer[] _layers = null!;
    private VirtualFileCatalog _catalog = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pathsToNormalize = Enumerable.Range(0, Operations)
            .Select(index => $"Ruleset\\Group-{index % 32:D2}\\Resource-{index:D4}.DAT")
            .ToArray();
        var unicodePaths = new[]
        {
            "ÜBER\\CAFÉ.DAT",
            "ΣΧΗΜΑ\\ΔΕΛΤΑ.DAT",
            "MUSIK\\ẞ.DAT",
            "TEMP\\KELVIN.DAT",
            "TÜRKİYE\\İ.DAT",
            "NORM\\CAFE\u0301.DAT",
            "SCRIPT\\𐐀.DAT",
        };
        _unicodePathsToNormalize = Enumerable.Range(0, Operations)
            .Select(index => unicodePaths[index % unicodePaths.Length])
            .ToArray();
        _pathsToFind = Enumerable.Range(0, Operations)
            .Select(index => index % 4 == 0
                ? $"missing/group-{index % 32:D2}/resource-{index:D4}.dat"
                : $"GROUP-{index % 32:D2}/RESOURCE-{index % 512:D4}.DAT")
            .ToArray();
        _layers = Enumerable.Range(0, 8)
            .Select(layerIndex => VirtualFileLayer.FromEntries(
                new VirtualFileProvenance($"layer-{layerIndex}", $"mod-{layerIndex}", $"layer-{layerIndex}"),
                Enumerable.Range(0, 512).Select(index => new VirtualFileSource(
                    $"group-{index % 32:D2}/resource-{index:D4}.dat",
                    $"layer-{layerIndex}-resource-{index:D4}"))))
            .ToArray();
        _catalog = new VirtualFileCatalog(_layers);
    }

    [Benchmark(OperationsPerInvoke = Operations)]
    [BenchmarkCategory("Path")]
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
    [BenchmarkCategory("UnicodePath")]
    public int NormalizeUnicodePaths()
    {
        var length = 0;
        foreach (var path in _unicodePathsToNormalize)
        {
            length += VirtualPath.NormalizeFile(path).Length;
        }

        return length;
    }

    [Benchmark(OperationsPerInvoke = Operations)]
    [BenchmarkCategory("ResolvedLookup")]
    public int LayeredLookup()
    {
        var found = 0;
        foreach (var path in _pathsToFind)
        {
            found += _catalog.TryGet(path, out _) ? 1 : 0;
        }

        return found;
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Operations)]
    [BenchmarkCategory("SliceLookup")]
    public int LegacyLayeredSliceLookup()
    {
        var found = 0;
        foreach (var path in _pathsToFind)
        {
            var slice = new VirtualFileEntry?[_layers.Length];
            for (var layerIndex = 0; layerIndex < _layers.Length; ++layerIndex)
            {
                _layers[layerIndex].TryGet(path, out slice[layerIndex]);
                found += slice[layerIndex] is null ? 0 : 1;
            }
        }

        return found;
    }

    [Benchmark(OperationsPerInvoke = Operations)]
    [BenchmarkCategory("SliceLookup")]
    public int CanonicalizedLayeredSliceLookup()
    {
        var found = 0;
        foreach (var path in _pathsToFind)
        {
            var slice = _catalog.GetSlice(path);
            for (var layerIndex = 0; layerIndex < slice.Count; ++layerIndex)
            {
                found += slice[layerIndex] is null ? 0 : 1;
            }
        }

        return found;
    }
}
