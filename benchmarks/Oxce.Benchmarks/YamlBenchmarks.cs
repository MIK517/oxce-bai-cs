using System.Text;
using BenchmarkDotNet.Attributes;
using Oxce.Formats.Yaml;

namespace Oxce.Benchmarks;

[MemoryDiagnoser]
public class YamlBenchmarks
{
    private const int LookupOperations = 256;
    private string _yaml = null!;
    private YamlMappingNode _mapping = null!;
    private string[] _lookupKeys = null!;

    [GlobalSetup]
    public void Setup()
    {
        var yaml = new StringBuilder(capacity: 32_000);
        for (var index = 0; index < 1_000; ++index)
        {
            yaml.Append("rule-").Append(index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture))
                .Append(": { type: item, cost: ").Append(index).Append(", enabled: true }\n");
        }

        _yaml = yaml.ToString();
        _mapping = (YamlMappingNode)YamlCompatibilityReader.Parse(_yaml, "benchmark.rul").Documents[0].Root;
        _lookupKeys = Enumerable.Range(0, LookupOperations)
            .Select(index => index % 4 == 0 ? $"missing-{index:D4}" : $"rule-{999 - index:D4}")
            .ToArray();
    }

    [Benchmark]
    public YamlDocumentSet ParseRepresentativeRuleset() =>
        YamlCompatibilityReader.Parse(_yaml, "benchmark.rul");

    [Benchmark(OperationsPerInvoke = LookupOperations)]
    public int RepeatedMappingLookup()
    {
        var found = 0;
        foreach (var key in _lookupKeys)
        {
            found += _mapping.TryGet(key, out _) ? 1 : 0;
        }

        return found;
    }
}
