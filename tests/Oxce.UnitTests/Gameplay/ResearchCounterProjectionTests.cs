using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class ResearchCounterProjectionTests
{
    [Fact]
    public void OrderedPrimaryChangesMatchNativeCounterDefaultsAndClamp()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "researchOrderedCounterRows"))
        {
            int? value = row[0].GetInt32() == -1 ? null : row[0].GetInt32();
            foreach (var increase in row[4].EnumerateArray()) value = CustomCounterValue.Apply(value, increase.GetBoolean());
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row[3].GetInt32())),
                CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(value)));
        }
    }
}
