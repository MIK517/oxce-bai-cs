using Oxce.Core.Diagnostics;
using Oxce.Core.Random;
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
/// second rewrite is byte-stable, and every written battle passes the conformance reader. S1 covers the battle root, S2
/// the map (its data sets loaded with the installation's terrain files), S3 the units and S4 the items.
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
            var (content, files, options) = Load(installation, master, addOn);
            var paths = Directory.EnumerateFiles(Path.Combine(saves, family.Replace('/', Path.DirectorySeparatorChar), "battlescape"))
                .Where(static path => path.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".asav", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(count, paths.Length);
            foreach (var path in paths) Verify(path, content, new BattleTerrainLoader(content.RuntimeRules.Battle, files), options);
        }
    }

    private static void Verify(string path, RuntimeContent content, BattleTerrainLoader terrain, OxceSaveLoadOptions options)
    {
        // The campaign's soldiers, which the soldier units are made from (SavedGame::getSoldier).
        var campaign = BattleRestoreCampaign.From(OxceSaveAdapter.LoadFile(path, content, new SplitMix64RandomSource(1), options).Campaign);
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
        // Nothing is skipped; a unit caught mid-panic (status 8) is what the reference's sanity check logs.
        Assert.All(diagnostics.Snapshot(), static item => Assert.Equal(OxceBattleSave.UnitStatusCode, item.Code));
        var battle = BattleState.Restore(snapshot, content, terrain, ufo, campaign);
        // Every corpus battle is past its first turn and has a deployment the readers find.
        Assert.True(battle.Turn >= 1, name);
        Assert.NotEqual("", battle.DeploymentId);
        var written = OxceBattleSave.Write(battle.Capture(), source);
        BattleSaveConformance.Check(written);
        Assert.Equal(Normalized(WithoutUnitQuirks(source)), Normalized(written));
        var text = Emit(written);
        var reread = (YamlMappingNode)YamlCompatibilityReader.Parse(text, name, ReadOptions).Documents[0].Root;
        var again = OxceBattleSave.Write(BattleState.Restore(OxceBattleSave.Read(reread, content), content, terrain, ufo, campaign).Capture(), reread);
        Assert.Equal(text, Emit(again));
    }

    // BattleUnit::save writes turnsSinceSpotted, turnsLeftSpottedForSnipers and turnsSinceStunned twice, the first two
    // first as the arrays' pointers; the load reads the first of each (useIndex), cut to its low byte. The port writes each
    // once with that value (ADR 0039), so the source is compared in that form.
    private static readonly string[] DuplicatedUnitKeys = ["turnsSinceSpotted", "turnsLeftSpottedForSnipers", "turnsSinceStunned"];

    private static YamlMappingNode WithoutUnitQuirks(YamlMappingNode battle) => new(battle.Span, battle.Entries.Select(entry =>
        entry.ScalarKey == "units" && entry.Value is YamlSequenceNode units
            ? entry with { Value = new YamlSequenceNode(units.Span, units.Items.Select(static unit => unit is YamlMappingNode map ? Once(map) : unit)) }
            : entry), battle.Tag, battle.Anchor);

    // The load also drops the AI module of a player's unit and of a dead or ignored one (SavedBattleGame::load, the units'
    // first pass), so the reference's own rewrite has no AI for them.
    private static YamlMappingNode Once(YamlMappingNode unit)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<YamlMappingEntry>();
        var dropAi = unit.ReadInt32("faction", -1) == 0 || unit.ReadInt32("status", 0) is 6 or 10;
        foreach (var entry in unit.Entries)
        {
            if (entry.ScalarKey == "AI" && dropAi) continue;
            if (entry.ScalarKey is not { } key || !DuplicatedUnitKeys.Contains(key)) entries.Add(entry);
            else if (seen.Add(key))
                entries.Add(entry with
                {
                    Value = new YamlScalarNode(entry.Value.Span,
                    (YamlValueReader.ReadInt32(entry.Value) & 0xFF).ToString(System.Globalization.CultureInfo.InvariantCulture), YamlScalarStyle.Plain)
                });
        }
        return new YamlMappingNode(unit.Span, entries, unit.Tag, unit.Anchor);
    }

    private static string Emit(YamlMappingNode node) =>
        YamlCompatibilityWriter.Emit(new YamlDocumentSet("battle.yaml", [new YamlDocument(node, node.Span)]));

    private static string Normalized(YamlMappingNode node) =>
        YamlSemanticNormalizer.NormalizeToJson(new YamlDocumentSet("battle.yaml", [new YamlDocument(node, node.Span)]));

    private static (RuntimeContent Content, IBattleTerrainFiles Files, OxceSaveLoadOptions Options) Load(string installation, string master, string addOn)
    {
        var cacheDirectory = Directory.CreateTempSubdirectory("oxce-private-battle-save-cache-");
        try
        {
            var request = InstallationLoadRequest.ForMasterAndAddOn(installation, master, addOn, new("Extended", "8.6.1.0"));
            var loaded = InstallationContentLoader.Load(request, new() { Cache = new() { DirectoryPath = cacheDirectory.FullName } },
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(loaded.IsSuccess, loaded.DescribeFailure());
            var options = new OxceSaveLoadOptions(request.MasterId, request.ActiveMods.ToHashSet(StringComparer.Ordinal), ReadOptions);
            return (loaded.Content!, new VirtualBattleTerrainFiles(loaded.VirtualFiles!), options);
        }
        finally { cacheDirectory.Delete(recursive: true); }
    }
}
