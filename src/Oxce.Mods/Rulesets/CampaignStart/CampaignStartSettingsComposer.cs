using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods.Loading;

namespace Oxce.Mods.Rulesets.CampaignStart;

internal static class CampaignStartSettingsComposer
{
    private static readonly IReadOnlyDictionary<string, StartingBaseVariant> BaseKeys =
        new Dictionary<string, StartingBaseVariant>(StringComparer.Ordinal)
        {
            ["startingBase"] = StartingBaseVariant.Default,
            ["startingBaseBeginner"] = StartingBaseVariant.Beginner,
            ["startingBaseExperienced"] = StartingBaseVariant.Experienced,
            ["startingBaseVeteran"] = StartingBaseVariant.Veteran,
            ["startingBaseGenius"] = StartingBaseVariant.Genius,
            ["startingBaseSuperhuman"] = StartingBaseVariant.Superhuman,
        };

    public static CampaignStartSettings Compose(ModLoadPlan plan, RulesetCompositionOptions options)
    {
        var builder = new CampaignStartSettingsBuilder();
        foreach (var group in plan.Groups)
        {
            foreach (var file in group.Rulesets)
            {
                using var input = file.OpenRead();
                var documents = YamlCompatibilityReader.Parse(input, file.SourcePath, options.Yaml);
                if (documents.Documents.Count == 0) continue;
                if (documents.Documents.Count != 1)
                    throw new YamlFormatException("Ruleset files must contain exactly one YAML document.",
                        documents.Documents[1].Span);
                if (documents.Documents[0].Root is YamlNullNode) continue;
                if (documents.Documents[0].Root is not YamlMappingNode root)
                    throw new YamlFormatException("Ruleset document root must be a mapping.", documents.Documents[0].Root.Span);
                Apply(builder, root);
            }
        }
        return new CampaignStartSettings(builder);
    }

    private static void Apply(CampaignStartSettingsBuilder b, YamlMappingNode root)
    {
        foreach (var pair in BaseKeys)
        {
            if (!root.TryGet(pair.Key, out var node)) continue;
            if (node is not YamlMappingNode mapping)
                throw new YamlFormatException($"Global rule '{pair.Key}' must be a mapping.", node!.Span);
            b.StartingBases[pair.Value] = b.StartingBases.TryGetValue(pair.Value, out var previous)
                ? Overlay(mapping, previous)
                : mapping;
        }
        if (root.TryGet("startingTime", out var time))
        {
            if (time is not YamlMappingNode mapping)
                throw new YamlFormatException("Global rule 'startingTime' must be a mapping.", time!.Span);
            b.Second = Read(mapping, "second", b.Second);
            b.Minute = Read(mapping, "minute", b.Minute);
            b.Hour = Read(mapping, "hour", b.Hour);
            b.Weekday = Read(mapping, "weekday", b.Weekday);
            b.Day = Read(mapping, "day", b.Day);
            b.Month = Read(mapping, "month", b.Month);
            b.Year = Read(mapping, "year", b.Year);
        }
        b.StartingDifficulty = Read(root, "startingDifficulty", b.StartingDifficulty);
        b.CostHireEngineer = Read(root, "costHireEngineer", b.CostHireEngineer);
        b.CostHireScientist = Read(root, "costHireScientist", b.CostHireScientist);
        b.CostEngineer = Read(root, "costEngineer", b.CostEngineer);
        b.CostScientist = Read(root, "costScientist", b.CostScientist);
        b.PersonnelTransferTime = Read(root, "timePersonnel", b.PersonnelTransferTime);
        b.HireByCountryOdds = Read(root, "hireByCountryOdds", b.HireByCountryOdds);
        b.HireByRegionOdds = Read(root, "hireByRegionOdds", b.HireByRegionOdds);
        b.InitialFunding = Read(root, "initialFunding", b.InitialFunding);
        if (root.TryGet("transferCosts", out var transfer))
        {
            if (transfer is not YamlMappingNode mapping)
                throw new YamlFormatException("Global rule 'transferCosts' must be a mapping.", transfer!.Span);
            b.GlobalTransferCostMultiplier = Read(mapping, "globalCostMult", b.GlobalTransferCostMultiplier);
            b.GlobalTransferCostDivisor = Read(mapping, "globalCostDiv", b.GlobalTransferCostDivisor);
        }
        b.PsiUnlockResearch = Read(root, "psiUnlockResearch", b.PsiUnlockResearch);
        b.FakeUnderwaterBaseUnlockResearch = Read(root, "fakeUnderwaterBaseUnlockResearch", b.FakeUnderwaterBaseUnlockResearch);
        b.NewBaseUnlockResearch = Read(root, "newBaseUnlockResearch", b.NewBaseUnlockResearch);
        b.HireScientistsUnlockResearch = Read(root, "hireScientistsUnlockResearch", b.HireScientistsUnlockResearch);
        b.HireEngineersUnlockResearch = Read(root, "hireEngineersUnlockResearch", b.HireEngineersUnlockResearch);
        ApplyNames(root, "hireScientistsRequiresBaseFunc", b.HireScientistsRequiredBaseFunctions, true);
        ApplyNames(root, "hireEngineersRequiresBaseFunc", b.HireEngineersRequiredBaseFunctions, true);
        b.DestroyedFacility = Read(root, "destroyedFacility", b.DestroyedFacility);
        b.DefeatScore = Read(root, "defeatScore", b.DefeatScore);
        b.DefeatFunds = Read(root, "defeatFunds", b.DefeatFunds);
        b.DifficultyDemigod = Read(root, "difficultyDemigod", b.DifficultyDemigod);
        ApplyNames(root, "baseNamesFirst", b.BaseNamesFirst, false);
        ApplyNames(root, "baseNamesMiddle", b.BaseNamesMiddle, false);
        ApplyNames(root, "baseNamesLast", b.BaseNamesLast, false);
        ApplyNames(root, "operationNamesFirst", b.OperationNamesFirst, false);
        ApplyNames(root, "operationNamesLast", b.OperationNamesLast, false);
    }

    private static YamlMappingNode Overlay(YamlMappingNode current, YamlMappingNode defaults)
    {
        var entries = current.Entries.ToList();
        foreach (var entry in defaults.Entries)
            if (entry.ScalarKey is null || !current.TryGet(entry.ScalarKey, out _)) entries.Add(entry);
        return new YamlMappingNode(current.Span, entries, current.Tag, current.Anchor);
    }

    private static void ApplyNames(YamlMappingNode root, string key, List<string> destination, bool unique)
    {
        if (root.TryGet(key, out var node)) CampaignStartYaml.ApplyEditableNames(destination, node!, unique);
    }

    private static int Read(YamlMappingNode mapping, string key, int current) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadInt32(node!) : current;
    private static bool Read(YamlMappingNode mapping, string key, bool current) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadBoolean(node!) : current;
    private static string Read(YamlMappingNode mapping, string key, string current) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadString(node!) : current;
    private static SourceSpan UnknownSpan(string sourcePath)
    {
        var position = new SourcePosition(1, 1, 0);
        return new SourceSpan(sourcePath, position, position);
    }
}
