using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Bootstrap;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 2 acceptance on the private active-battle saves (vanilla UFO, TFTD and Rosigma <c>battlescape/</c>):
/// each <c>battleGame</c> loads into <see cref="BattleState"/> and rewrites over its source with semantic equality, a
/// second rewrite is byte-stable, and every written battle passes the conformance reader. S1 covers the battle root and S2
/// the map (its data sets loaded with the installation's terrain files); the units and items keys stay as the source has
/// them until S3 and S4.
/// </summary>
public sealed class PrivateBattleSaveTests
{
    private static readonly (string Master, string AddOn, string Family, int Saves)[] Families =
    [
        ("xcom1", "-", "vanilla-ufo", 3), ("xcom2", "-", "vanilla-tftd", 2), ("40k", "40k_ROSIGMA_edits", "modded/rosigma", 3),
    ];

    private static readonly YamlReadOptions ReadOptions = new() { MaxBytes = 32 * 1024 * 1024, MaxNodes = 2_000_000 };

    [Fact]
    public void ActiveBattleRootsLoadAndRewriteToTheirSource()
    {
        var installation = TestFixtures.RequirePrivateInstallation();
        var saves = TestFixtures.RepositoryPath("fixtures", "private", "saves");
        Assert.SkipUnless(Directory.Exists(saves), "The private save corpus is not available in this checkout.");
        foreach (var (master, addOn, family, count) in Families)
        {
            var (content, files) = Load(installation, master, addOn);
            var paths = Directory.EnumerateFiles(Path.Combine(saves, family.Replace('/', Path.DirectorySeparatorChar), "battlescape"))
                .Where(static path => path.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".asav", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(count, paths.Length);
            foreach (var path in paths) Verify(path, content, new BattleTerrainLoader(content.RuntimeRules.Battle, files));
        }
    }

    private static void Verify(string path, RuntimeContent content, BattleTerrainLoader terrain)
    {
        var name = Path.GetFileName(path);
        var documents = YamlCompatibilityReader.Parse(File.ReadAllText(path), name, ReadOptions);
        var body = (YamlMappingNode)documents.Documents[1].Root;
        Assert.True(body.TryGet("battleGame", out var node) && node is YamlMappingNode, name);
        var source = (YamlMappingNode)node!;
        // The UFO the battle is at (Ufo inBattlescape), from which the readers derive a UFO mission's deployment.
        var ufo = body.TryGet("ufos", out var ufos) && ufos is YamlSequenceNode list
            ? list.Items.OfType<YamlMappingNode>().FirstOrDefault(static map => map.ReadBoolean("inBattlescape", false))?.ReadString("type", "") ?? ""
            : "";
        var diagnostics = new DiagnosticCollector(100);
        var snapshot = OxceBattleSave.Read(source, content, diagnostics);
        Assert.Empty(diagnostics.Snapshot());
        var battle = BattleState.Restore(snapshot, content, terrain, ufo);
        // Every corpus battle is past its first turn and has a deployment the readers find.
        Assert.True(battle.Turn >= 1, name);
        Assert.NotEqual("", battle.DeploymentId);
        var written = OxceBattleSave.Write(battle.Capture(), source);
        BattleSaveConformance.Check(written);
        Assert.Equal(Normalized(source), Normalized(written));
        var text = Emit(written);
        var reread = (YamlMappingNode)YamlCompatibilityReader.Parse(text, name, ReadOptions).Documents[0].Root;
        var again = OxceBattleSave.Write(BattleState.Restore(OxceBattleSave.Read(reread, content), content, terrain, ufo).Capture(), reread);
        Assert.Equal(text, Emit(again));
    }

    private static string Emit(YamlMappingNode node) =>
        YamlCompatibilityWriter.Emit(new YamlDocumentSet("battle.yaml", [new YamlDocument(node, node.Span)]));

    private static string Normalized(YamlMappingNode node) =>
        YamlSemanticNormalizer.NormalizeToJson(new YamlDocumentSet("battle.yaml", [new YamlDocument(node, node.Span)]));

    private static (RuntimeContent Content, IBattleTerrainFiles Files) Load(string installation, string master, string addOn)
    {
        var cacheDirectory = Directory.CreateTempSubdirectory("oxce-private-battle-save-cache-");
        try
        {
            var request = InstallationLoadRequest.ForMasterAndAddOn(installation, master, addOn, new("Extended", "8.6.1.0"));
            var loaded = InstallationContentLoader.Load(request, new() { Cache = new() { DirectoryPath = cacheDirectory.FullName } },
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(loaded.IsSuccess, loaded.DescribeFailure());
            return (loaded.Content!, new VirtualBattleTerrainFiles(loaded.VirtualFiles!));
        }
        finally { cacheDirectory.Delete(recursive: true); }
    }
}
