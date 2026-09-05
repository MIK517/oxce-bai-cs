using System.Text.Json;
using Oxce.Gameplay.Campaigns;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicTimeFixtureTests
{
    [Fact]
    public void DispatcherMatchesExtractedReferenceLoopIncludingPauseFallthrough()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Oxce.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,
            "fixtures", "expected", "savegames", "strategic-time.expected.json")));
        CampaignTime[] starts = [new(1, 1, 1, 1999, 0, 0, 0), new(1, 1, 1, 1999, 0, 9, 55),
            new(1, 1, 1, 1999, 0, 29, 55), new(1, 1, 1, 1999, 0, 59, 55),
            new(1, 1, 1, 1999, 23, 59, 55), new(1, 31, 1, 1999, 23, 59, 55)];
        foreach (var scenario in expected.RootElement.GetProperty("cases").EnumerateArray())
        {
            var effects = new Effects(scenario.GetProperty("pauseAt").GetInt32());
            var result = CampaignTimeDispatcher.Advance(starts[scenario.GetProperty("trigger").GetInt32()], 2, effects);
            Assert.Equal(scenario.GetProperty("ticks").GetInt32(), result.Summary.TickCount);
            Assert.Equal(scenario.GetProperty("trace").EnumerateArray().Select(value => value.GetInt32()), effects.Trace);
        }
    }

    private sealed class Effects(int pauseAt) : ICampaignTimeEffects
    {
        public List<int> Trace { get; } = [];
        public string? Preflight(CampaignTime next, CampaignTimeTrigger highestTrigger) => null;
        public bool Apply(CampaignTime current, CampaignTimeTrigger trigger)
        {
            Trace.Add((int)trigger);
            return (int)trigger == pauseAt;
        }
    }
}
