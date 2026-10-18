using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;

namespace Oxce.Savegames.Oxce;

public static partial class OxceSaveAdapter
{
    private static ReadOnlyCollection<ResearchDiaryEntrySnapshot> ReadResearchDiary(YamlMappingNode body, RuntimeContent content)
    {
        var result = new List<ResearchDiaryEntrySnapshot>();
        var index = 0;
        foreach (var map in Maps(body, "researchDiary"))
        {
            if (index >= CampaignState.MaximumResearchDiaryEntries)
                throw new InvalidDataException("Research diary exceeds the supported population limit.");
            var slot = index++;
            var id = map.ReadString("name", string.Empty);
            // SavedGame::load skips removed topics before loading their diary data.
            if (!content.RuntimeRules.Research.TryGet(id, out _)) continue;
            var key = map.ReadString("oxcePortEntityKey", $"research-diary:legacy:{slot}");
            if (!map.TryGet("date", out var dateNode) || dateNode is not YamlSequenceNode date || date.Items.Count < 3)
                throw new InvalidDataException("Research diary date requires three unsigned values.");
            if (!map.TryGet("sourceType", out var sourceNode))
                throw new InvalidDataException("Research diary source type is missing.");
            var source = YamlValueReader.ReadUInt64(sourceNode!);
            if (source > byte.MaxValue) throw new InvalidDataException("Research diary source type exceeds its byte range.");
            result.Add(new(id, DiaryDate(date.Items[0]), DiaryDate(date.Items[1]), DiaryDate(date.Items[2]),
                (byte)source, map.ReadString("sourceName", string.Empty))
            { PreservationKey = key });
        }
        return Array.AsReadOnly(result.ToArray());

        static uint DiaryDate(YamlNode node)
        {
            var value = YamlValueReader.ReadUInt64(node);
            return value <= uint.MaxValue ? (uint)value : throw new InvalidDataException("Research diary date exceeds its unsigned range.");
        }
    }

    private static YamlSequenceNode? WriteResearchDiary(IReadOnlyList<ResearchDiaryEntrySnapshot> diary, YamlMappingNode? original)
    {
        if (diary.Count == 0) return null;
        if (diary.Count > CampaignState.MaximumResearchDiaryEntries)
            throw new InvalidDataException("Research diary exceeds the supported population limit.");
        var currentRules = new Dictionary<string, string>(StringComparer.Ordinal);
        var currentRuleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in diary)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.RuleId) || entry.SourceName is null || entry.PreservationKey is null)
                throw new InvalidDataException("Research diary entry has invalid research or source data.");
            if (entry.PreservationKey.Length != 0 && !currentRules.TryAdd(entry.PreservationKey, entry.RuleId))
                throw new InvalidDataException("Duplicate research diary preservation keys.");
            currentRuleIds.Add(entry.RuleId);
        }
        var sources = new Dictionary<string, YamlMappingNode>(StringComparer.Ordinal);
        var index = 0;
        if (original is not null)
            foreach (var map in Maps(original, "researchDiary"))
            {
                if (index >= CampaignState.MaximumResearchDiaryEntries)
                    throw new InvalidDataException("Research diary exceeds the supported population limit.");
                var slot = index++;
                var id = map.ReadString("name", string.Empty);
                if (!currentRuleIds.Contains(id)) continue;
                var key = map.ReadString("oxcePortEntityKey", $"research-diary:legacy:{slot}");
                if (!currentRules.TryGetValue(key, out var currentId) || id != currentId) continue;
                if (key.Length != 0 && !sources.TryAdd(key, map))
                    throw new InvalidDataException("Duplicate research diary preservation keys.");
            }
        return Sequence(diary.Select(entry =>
        {
            var source = entry.PreservationKey.Length != 0 && sources.TryGetValue(entry.PreservationKey, out var candidate) &&
                candidate.ReadString("name", string.Empty) == entry.RuleId ? candidate : null;
            return Overlay(source,
            [
                Pair("name", Scalar(entry.RuleId)),
                Pair("date", Sequence(new[] { entry.Year, entry.Month, entry.Day }.Select(value => ULong(value)))),
                Pair("sourceType", Integer(entry.SourceType)), Pair("sourceName", Scalar(entry.SourceName)),
                Pair("oxcePortEntityKey", Scalar(entry.PreservationKey)),
            ]);
        }));
    }
}
