using System.Text;
using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldEscortSpawnTests
{
    [Fact]
    public void EscortInitializationMatchesPinnedSavedOrderOriginBackupAndSpeed()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "spawnEscortRows"))
        {
            var hunter = row[0].GetBoolean();
            var requested = row[1].GetBoolean();
            var variant = row[2].GetInt32();
            var status = row[3].GetInt32();
            var source = new UfoSnapshot(9, "UFO", 4, "TRAJ", 0, 0.3, 0.1, UfoStatus.Flying, WorldAltitudes.Low)
            {
                HunterKiller = hunter, Speed = 900,
                Destination = WorldTargetReference.ForWaypoint(0, new(0.4, 0.2)),
            };
            var first = source with
            {
                UniqueId = 10, Longitude = 0.7, Latitude = 0.3, HunterKiller = variant == 2,
                MissionId = variant == 1 ? 5 : 4, Status = (UfoStatus)status,
            };
            var second = source with { UniqueId = 11, Longitude = 0.9, Latitude = 0.4, HunterKiller = false };
            UfoSnapshot[] targets = variant == 0 ? [] : [first, second];
            var result = WorldHunting.InitializeEscort(source, requested, 2200, targets);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object?[]
            {
                hunter, requested, variant, status, result.Escort, result.Escorting,
                result.Escorting ? result.Destination!.UniqueId : 0,
                result.Longitude, result.Latitude, result.Speed,
                result.OriginalWaypoint?.Longitude, result.OriginalWaypoint?.Latitude,
            });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())),
                CanonicalJson.Normalize(actual));
        }
    }
}
