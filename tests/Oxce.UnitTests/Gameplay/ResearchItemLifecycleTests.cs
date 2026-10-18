using System.Text.Json;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class ResearchItemLifecycleTests
{
    [Fact]
    public void HeldItemAndCorpseRecoveryKeepBothNativeCallers()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "researchCorpseRows"))
        {
            var need = row[1].GetBoolean();
            var destroy = row[2].GetBoolean();
            var returns = row[3].GetBoolean();
            var finished = row[4].GetBoolean();
            var held = ResearchItemLifecycle.ReturnsHeldItem(need && (destroy || returns), returns, finished) ? 1 : 0;
            var corpses = ResearchItemLifecycle.CorpseQuantity(row[0].GetBoolean(), need, destroy, returns, finished,
                row[8].GetBoolean(), row[5].GetBoolean() && row[6].GetBoolean() && row[7].GetBoolean());
            TestFixtures.AssertJsonEqual(row.EnumerateArray().Skip(9).ToArray(), JsonSerializer.SerializeToUtf8Bytes(new[] { held, corpses }));
        }
    }
}
