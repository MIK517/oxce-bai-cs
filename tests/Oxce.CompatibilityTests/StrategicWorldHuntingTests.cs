using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldHuntingTests
{
    [Fact]
    public void AttractionMatchesPinnedMethods()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var actual = new List<int>();
        var expected = new List<int>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "attractionRows"))
        {
            Assert.True(WorldHunting.TryAttraction(row[0].GetInt32(), row[1].GetBoolean(), row[2].GetInt32(),
                row[3].GetInt32(), row[4].GetInt32(), 100, out var attraction));
            actual.Add(attraction);
            expected.Add(row[5].GetInt32());
        }
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(actual)));
    }

    [Fact]
    public void SelectionMatchesPinnedSavedOrderAndOriginalTargetRules()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "selectionRows"))
        {
            var mode = row[0].GetInt32();
            var variant = row[1].GetInt32();
            var a = new WorldTargetReference(WorldTargetKind.Craft, "A", 1, 0, WorldGeometry.Nautical(100));
            var b = new WorldTargetReference(WorldTargetKind.Craft, "B", 2, 0, WorldGeometry.Nautical(100));
            if (variant == 4)
            {
                a = a with { Latitude = WorldGeometry.Nautical(301) };
                b = b with { Latitude = WorldGeometry.Nautical(300) };
            }
            Assert.True(WorldHunting.TryAttraction(mode, false, 0, 2000, 0, 100, out var attractionA));
            Assert.True(WorldHunting.TryAttraction(mode, false, 0, variant == 1 ? 2000 : 6000, 0, 100, out var attractionB));
            var ufo = new UfoSnapshot(9, "UFO", 4, "TRAJ", 0, 0, 0, UfoStatus.Flying, WorldAltitudes.Low)
            { Hunting = variant is 1 or 2 or 6, Destination = a };
            WorldHuntCraft[] crafts =
            [
                new(a, a.Position, attractionA, variant is 3 or 6, variant == 2, true),
                new(b, b.Position, attractionB, variant == 2, variant == 3, true),
            ];
            Assert.Equal(row[2].GetInt32(), WorldHunting.Select(ufo, variant == 5 ? 0 : 300, crafts)?.Id ?? 0);
        }
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(0, 100, 2147483647)]
    [InlineData(1, 100, 2147483647)]
    public void MalformedAttractionArithmeticIsRejected(int mode, int damageMaximum, int units) =>
        Assert.False(WorldHunting.TryAttraction(mode, true, units, 6000, 50, damageMaximum, out _));
}
