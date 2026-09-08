using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Oxce.Formats.Yaml;
using Oxce.Mods.Files;
using Oxce.Mods.Rulesets.PersonnelTactical;

namespace Oxce.Mods.Rulesets.Runtime;

public sealed record RuntimeSoldierNamePool(
    string Source,
    IReadOnlyList<string> MaleFirst, IReadOnlyList<string> FemaleFirst,
    IReadOnlyList<string> MaleLast, IReadOnlyList<string> FemaleLast,
    IReadOnlyList<string> MaleCallsign, IReadOnlyList<string> FemaleCallsign,
    IReadOnlyList<int> LookWeights, int FemaleFrequency, int GlobalWeight,
    string Country, string Region)
{
    public string ContentHash { get; init; } = string.Empty;
}

internal static class RuntimeSoldierNamePoolLoader
{
    internal static string ComputeHash(VirtualFileEntry file) => Convert.ToHexString(SHA256.HashData(ReadBytes(file)));

    private static byte[] ReadBytes(VirtualFileEntry file)
    {
        const int maximumBytes = 16 * 1024 * 1024;
        using var input = file.OpenRead();
        using var output = new MemoryStream();
        Span<byte> buffer = stackalloc byte[8192];
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            if (output.Length + count > maximumBytes) throw new InvalidDataException("Soldier name pool exceeds the 16 MiB limit.");
            output.Write(buffer[..count]);
        }
        return output.ToArray();
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<RuntimeSoldierNamePool>> Load(
        TypedRuleSection<SoldierRule> soldiers, VirtualFileCatalog files)
    {
        var result = new Dictionary<string, IReadOnlyList<RuntimeSoldierNamePool>>(StringComparer.Ordinal);
        var loaded = new Dictionary<string, RuntimeSoldierNamePool>(StringComparer.Ordinal);
        foreach (var soldier in soldiers.Rules)
        {
            var pools = new List<RuntimeSoldierNamePool>();
            foreach (var name in soldier.Value.SoldierNames)
            {
                if (string.IsNullOrEmpty(name)) throw new InvalidDataException("Soldier name pool path cannot be empty.");
                IEnumerable<string> paths = name.EndsWith('/')
                    ? files.List(name).Where(p => p.EndsWith(".nam", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p => name + p)
                    : [name];
                foreach (var path in paths)
                {
                    var file = files.GetRequired(path);
                    if (!loaded.TryGetValue(file.CanonicalPath, out var pool))
                    {
                        var bytes = ReadBytes(file);
                        using var input = new MemoryStream(bytes, writable: false);
                        var yaml = YamlCompatibilityReader.Parse(input, file.SourcePath);
                        if (yaml.Documents.Count != 1 || yaml.Documents[0].Root is not YamlMappingNode node)
                            throw new InvalidDataException($"Name pool '{path}' requires one YAML mapping.");
                        pool = Read(file.CanonicalPath, node) with { ContentHash = Convert.ToHexString(SHA256.HashData(bytes)) };
                        loaded.Add(file.CanonicalPath, pool);
                    }
                    pools.Add(pool);
                    if (pools.Count > 10_000) throw new InvalidDataException("Soldier name pool count exceeds the limit.");
                }
            }
            result.Add(soldier.Id, pools.AsReadOnly());
        }
        return new ReadOnlyDictionary<string, IReadOnlyList<RuntimeSoldierNamePool>>(result);
    }

    private static RuntimeSoldierNamePool Read(string source, YamlMappingNode node)
    {
        var maleFirst = Strings("maleFirst");
        if (maleFirst.Count == 0) throw new InvalidDataException($"Name pool '{source}' has an empty maleFirst list.");
        var maleLast = Strings("maleLast");
        var maleCallsign = Strings("maleCallsign");
        var femaleFirst = Strings("femaleFirst");
        var femaleLast = Strings("femaleLast");
        var femaleCallsign = Strings("femaleCallsign");
        var weights = Sequence("lookWeights").Select(YamlValueReader.ReadInt32).ToList();
        if (weights.Any(w => w < 0)) throw new InvalidDataException($"Name pool '{source}' has negative look weights.");
        var globalWeight = Integer("globalWeight", 100);
        return new(source, maleFirst, femaleFirst.Count == 0 ? maleFirst : femaleFirst,
            maleLast, femaleLast.Count == 0 ? maleLast : femaleLast,
            maleCallsign, femaleCallsign.Count == 0 ? maleCallsign : femaleCallsign,
            weights.AsReadOnly(), Integer("femaleFrequency", -1), globalWeight <= 0 ? 100 : globalWeight,
            String("country"), String("region"));

        IReadOnlyList<string> Strings(string key) => Array.AsReadOnly(Sequence(key).Select(YamlValueReader.ReadString).ToArray());
        IReadOnlyList<YamlNode> Sequence(string key) => !node.TryGet(key, out var value) ? [] :
            value is YamlSequenceNode sequence ? sequence.Items : throw new InvalidDataException($"Name pool '{source}' {key} must be a sequence.");
        int Integer(string key, int fallback) => node.TryGet(key, out var value) ? YamlValueReader.ReadInt32(value!) : fallback;
        string String(string key) => node.TryGet(key, out var value) ? YamlValueReader.ReadString(value!) : string.Empty;
    }
}
