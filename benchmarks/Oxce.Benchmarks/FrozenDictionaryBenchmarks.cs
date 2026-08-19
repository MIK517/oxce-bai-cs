using System.Collections.Frozen;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Oxce.Benchmarks;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class FrozenDictionaryBenchmarks
{
    private const int LookupOperations = 1_024;
    private Dictionary<string, int> _source = null!;
    private Dictionary<string, int> _dictionary = null!;
    private FrozenDictionary<string, int> _frozen = null!;
    private string[] _lookupKeys = null!;

    [Params(2_000, 16_000)]
    public int EntryCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _source = Enumerable.Range(0, EntryCount).ToDictionary(
            index => $"group-{index % 64:D2}/resource-{index:D5}.dat",
            static index => index,
            StringComparer.Ordinal);
        _dictionary = new Dictionary<string, int>(_source, StringComparer.Ordinal);
        _frozen = _source.ToFrozenDictionary(StringComparer.Ordinal);
        _lookupKeys = Enumerable.Range(0, LookupOperations)
            .Select(index => index % 4 == 0
                ? $"missing/resource-{index:D5}.dat"
                : $"group-{index % 64:D2}/resource-{index % EntryCount:D5}.dat")
            .ToArray();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Construction")]
    public Dictionary<string, int> BuildDictionary() =>
        new(_source, StringComparer.Ordinal);

    [Benchmark]
    [BenchmarkCategory("Construction")]
    public FrozenDictionary<string, int> BuildFrozenDictionary() =>
        _source.ToFrozenDictionary(StringComparer.Ordinal);

    [Benchmark(Baseline = true, OperationsPerInvoke = LookupOperations)]
    [BenchmarkCategory("Lookup")]
    public int DictionaryLookup()
    {
        var sum = 0;
        foreach (var key in _lookupKeys)
        {
            _dictionary.TryGetValue(key, out var value);
            sum += value;
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = LookupOperations)]
    [BenchmarkCategory("Lookup")]
    public int FrozenDictionaryLookup()
    {
        var sum = 0;
        foreach (var key in _lookupKeys)
        {
            _frozen.TryGetValue(key, out var value);
            sum += value;
        }

        return sum;
    }
}
