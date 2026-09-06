using System.Text.Json;
using Oxce.Gameplay.Campaigns;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicLogisticsFixtureTests
{
    [Fact]
    public void LogisticsArithmeticMatchesExtractedReferenceMethods()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Oxce.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,
            "fixtures", "expected", "savegames", "strategic-logistics.expected.json")));
        foreach (var row in expected.RootElement.GetProperty("prices").EnumerateArray())
            Assert.Equal(row[2].GetInt32(), StrategicLogisticsMath.AdjustedItemPrice(row[0].GetInt32(), row[1].GetInt32()));
        foreach (var row in expected.RootElement.GetProperty("stores").EnumerateArray())
            Assert.Equal(row[1].GetInt32() != 0, StrategicLogisticsMath.StoresOverfull(10, row[0].GetDouble()));
        foreach (var row in expected.RootElement.GetProperty("transfers").EnumerateArray())
        {
            var distance = StrategicLogisticsMath.TransferDistance(0, 0, row[0].GetDouble(), 0);
            Assert.Equal(row[1].GetDouble(), distance, 10);
            Assert.Equal(row[2].GetInt32(), StrategicLogisticsMath.TransferHours(distance));
            Assert.Equal(row[3].GetInt32(), StrategicLogisticsMath.TransferUnitCost(distance, CampaignTransferKind.Item));
            Assert.Equal(row[4].GetInt32(), StrategicLogisticsMath.TransferUnitCost(distance, CampaignTransferKind.Soldier));
            Assert.Equal(row[5].GetInt32(), StrategicLogisticsMath.TransferUnitCost(distance, CampaignTransferKind.Craft));
        }
    }
}
