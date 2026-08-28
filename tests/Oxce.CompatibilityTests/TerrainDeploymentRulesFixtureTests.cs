using System.Text.Json;
using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.TerrainDeployment;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class TerrainDeploymentRulesFixtureTests
{
    [Fact]
    public void TerrainDeploymentRulesMatchPinnedReferenceFixture()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "terrain-deployment-rules");
        var expectedPath = Path.Combine(root, "fixtures", "expected", "mods", "terrain-deployment-rules.expected.json");
        var diagnostics = new DiagnosticCollector();
        var discovery = ModDiscovery.ScanDirectory(fixture, diagnostics);
        var plan = ModLoadPlanner.Create(ModCatalog.Create(discovery.Mods, diagnostics),
            [new ModActivation("fixture", true)], "fixture", new ModEngineIdentity("Extended", "8.6.1.0"), diagnostics);
        var actual = TerrainDeploymentRuleCatalog.Load(plan, diagnostics);
        using var expected = JsonDocument.Parse(File.ReadAllText(expectedPath));

        var terrain = Assert.Single(actual.Terrains.Rules).Value;
        Assert.Equal(expected.RootElement.GetProperty("terrain").EnumerateArray().Select(value => value.GetInt32()),
            new[] { terrain.MapBlocks.Count, terrain.CivilianTypes.Count, terrain.MinimumDepth, terrain.MaximumDepth });
        var command = Assert.Single(actual.MapScripts["MAP"].Commands);
        Assert.Equal(expected.RootElement.GetProperty("mapCommand").EnumerateArray().Select(value => value.GetInt32()),
            new[] { command.Size[0], command.Size[1], command.Size[2], command.Frequencies[0], command.Label });
        Assert.Equal(expected.RootElement.GetProperty("replacement").EnumerateArray().Select(value => value.GetInt32()),
            Assert.Single(actual.MapScripts["REPLACED"].Commands).Size);
        var patch = actual.McdPatches["URBAN"];
        var patchExpected = expected.RootElement.GetProperty("mcdPatch").EnumerateArray().ToArray();
        Assert.Equal(patchExpected[0].GetInt32(), patch.Entries.Count);
        Assert.Equal(patchExpected[1].GetInt32(), patch.Entries[0].Integers["bigWall"]);
        Assert.Equal(patchExpected[2].GetBoolean(), patch.Entries[1].Booleans["noFloor"]);
        Assert.Equal(patchExpected[3].GetInt32(), patch.Entries[1].Lofts!.Count);
        var race = Assert.Single(actual.AlienRaces.Rules).Value;
        Assert.Equal(expected.RootElement.GetProperty("race")[0].GetInt32(), race.ListOrder);
        Assert.Equal(expected.RootElement.GetProperty("race")[1].GetUInt64(), Assert.Single(race.RetaliationMissionWeights).Value["MISSION"]);
        var environment = Assert.Single(actual.EnviroEffects.Rules).Value.EnvironmentalConditions["STR_HOSTILE"];
        Assert.Equal(expected.RootElement.GetProperty("environment")[0].GetInt32(), environment.ChancePerTurn);
        Assert.Equal(expected.RootElement.GetProperty("environment")[1].GetInt32(), environment.Color);
        var condition = Assert.Single(actual.StartingConditions.Rules).Value;
        Assert.Equal(expected.RootElement.GetProperty("startingCondition")[0].GetInt32(), condition.NameCollections["allowedItems"].Count);
        Assert.Equal(expected.RootElement.GetProperty("startingCondition")[1].GetInt32(), condition.RequiredItems["ITEM"]);
        var deployment = Assert.Single(actual.AlienDeployments.Rules).Value;
        Assert.Equal(expected.RootElement.GetProperty("deployment").EnumerateArray().Select(value => value.GetInt32()),
            new[] { deployment.Depth[0], deployment.Depth[1], deployment.DeploymentData.Count });
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Severity >= DiagnosticSeverity.Error);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
