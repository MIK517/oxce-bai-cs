using System.Text.Json;
using System.Text.Json.Nodes;
using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Content;

namespace Oxce.Gameplay.Campaigns;

/// <summary>
/// Test-only comparison of campaign snapshots for staged ticks (ADR 0035): a shadow copy must start from the
/// original's state and reach the same state at every stage point. Snapshots are compared as a save would keep
/// them: each is restored once and captured again, so repairs that loading makes and the next tick would make anyway
/// (a UFO still aimed at a site that just expired) do not count, and as JSON with object properties in ordinal order,
/// so dictionary enumeration order does not matter.
/// </summary>
internal static class StagingVerification
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = false, MaxDepth = 256 };

    public static string Canonical(CampaignSnapshot snapshot, RuntimeContent content)
    {
        var restored = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(snapshot.RandomState)).Capture();
        return Sort(JsonSerializer.SerializeToNode(restored, Options))?.ToJsonString() ?? "null";
    }

    public static void AssertSame(string expected, CampaignSnapshot actual, RuntimeContent content, string what)
    {
        var actualJson = Canonical(actual, content);
        if (expected == actualJson) return;
        var index = 0;
        while (index < expected.Length && index < actualJson.Length && expected[index] == actualJson[index]) index++;
        var start = Math.Max(0, index - 120);
        throw new InvalidOperationException(
            $"A staged tick's campaign differs from {what} near: {Excerpt(actualJson, start)} (expected {Excerpt(expected, start)}).");
    }

    private static string Excerpt(string text, int start) => text.Substring(start, Math.Min(240, text.Length - start));

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject value => new JsonObject(value.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => KeyValuePair.Create(pair.Key, Sort(pair.Value?.DeepClone())))),
        JsonArray array => new JsonArray([.. array.Select(static item => Sort(item?.DeepClone()))]),
        _ => node?.DeepClone(),
    };
}
