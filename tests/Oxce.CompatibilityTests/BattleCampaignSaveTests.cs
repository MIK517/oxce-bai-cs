using Oxce.Core.Random;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Savegames.Oxce;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 2 S5: the campaign's battle in its saves. <c>SavedGame::load</c> and <c>save</c> read and write
/// <c>battleGame</c> through the battle save module and the header's <c>mission</c>, <c>target</c>, <c>craftOrBase</c>
/// and <c>turn</c> (<c>src/Savegame/SavedGame.cpp</c> at 4df3a5e); <c>StartBattle</c> sets what
/// <c>BattlescapeGenerator</c>'s setters and <c>deployXCOM</c> set on the campaign (the <c>inBattlescape</c> flags of
/// <c>Craft</c>, <c>Ufo</c>, <c>MissionSite</c>, <c>AlienBase</c> and <c>Base</c>, the soldiers' battle armor with
/// <c>transformedArmor</c> or <c>replacedArmor</c>); a battle before its first turn is not saved.
/// </summary>
public sealed class BattleCampaignSaveTests
{
    private static (CampaignState Campaign, BattleState Battle) Started(string deployment = "UFO_SCOUT")
    {
        var (campaign, _) = BattleStartTests.Requested(BattleStartTests.Rules, deployment);
        BattleStartTests.Started(campaign);
        return (campaign, BattleStartTests.Battle(campaign));
    }

    private static OxceSaveLoadOptions Options(CampaignState campaign, bool terrain = true) =>
        new(campaign.Identity.MasterId, campaign.Identity.ActiveMods.ToHashSet(StringComparer.Ordinal))
        {
            BattleTerrainFiles = terrain ? BattleStartTests.TerrainFiles() : null,
        };

    /// <summary>The campaign's snapshot with its battle past the first turn (what startFirstTurn would leave: turn 1).</summary>
    private static CampaignSnapshot AtFirstTurn(CampaignState campaign)
    {
        var snapshot = campaign.Capture();
        return snapshot with { Battle = snapshot.Battle! with { Turn = 1 } };
    }

    private static LoadedOxceCampaign Load(string text, CampaignState campaign, bool terrain = true) =>
        OxceSaveAdapter.Load(text, "battle.sav", campaign.Content, new SplitMix64RandomSource(5), Options(campaign, terrain));

    private static YamlMappingNode Document(string text, int index) =>
        (YamlMappingNode)YamlCompatibilityReader.Parse(text, "battle.sav").Documents[index].Root;

    private static YamlMappingNode Craft(YamlMappingNode body) => Mappings(Mappings(body, "bases")[0], "crafts")
        .Single(static craft => craft.ReadString("type", "") == "SHIP");

    private static YamlMappingNode[] Mappings(YamlMappingNode owner, string key) =>
        owner.TryGet(key, out var node) && node is YamlSequenceNode sequence ? [.. sequence.Items.Cast<YamlMappingNode>()] : [];

    private static BattleState LoadedBattle(CampaignState campaign) => BattleStartTests.Battle(campaign);

    [Fact]
    public void ATurnZeroBattleIsRefusedAndARestoredBattleIsSavedAndReloaded()
    {
        var (campaign, _) = Started();
        // No save before the first turn: the reference saves only after the briefing and startFirstTurn.
        Assert.Equal(CampaignBattles.BattleSaveBeforeFirstTurn.Text, campaign.QuerySaveRefusal());
        Assert.Null(campaign.CaptureForSave(out var refusal));
        Assert.Equal(CampaignBattles.BattleSaveBeforeFirstTurn.Text, refusal);

        var snapshot = AtFirstTurn(campaign);
        var text = OxceSaveAdapter.EmitNewCampaign(snapshot);
        // SavedGame::save: the header's battle lines after the time, craftOrBase quoted and escaped.
        var header = Document(text, 0);
        Assert.Equal(["name", "version", "engine", "build", "time", "mission", "target", "craftOrBase", "turn", "mods"],
            header.Entries.Select(static entry => entry.ScalarKey).Take(10));
        Assert.Equal(("STR_UFO_GROUND_ASSAULT", "", "1"), (header.ReadString("mission", "?"), header.ReadString("target", "?"),
            header.ReadString("turn", "?")));
        Assert.Equal(YamlScalarStyle.DoubleQuoted, Assert.IsType<YamlScalarNode>(header.Entries.Single(static entry => entry.ScalarKey == "craftOrBase").Value).Style);
        // The body's battleGame is the battle save module's and passes the conformance reader.
        var body = Document(text, 1);
        Assert.True(body.TryGet("battleGame", out var battleNode));
        BattleSaveConformance.Check((YamlMappingNode)battleNode!);
        Assert.True(header.ReadInt32("turn", 0) >= 1);

        var loaded = Load(text, campaign);
        var restored = loaded.Campaign;
        // The battle is back: the campaign holds for it, and it can be saved again.
        Assert.Equal(CampaignBattles.BattleInProgress.Text,
            Assert.Single(restored.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>()).Reason);
        Assert.Null(restored.QuerySaveRefusal());
        Assert.NotNull(restored.CaptureForSave(out _));
        // The saved state reloads as the load sets it (resetUnitTiles' visibility); the deployment is derived from the UFO
        // in the battlescape (the ground assault's mission type names none).
        Assert.Equivalent(BattleSaveUnitTests.Loaded(snapshot.Battle!), restored.Capture().Battle, strict: true);
        Assert.Equal("UFO_SCOUT", LoadedBattle(restored).DeploymentId);
        // A loaded rewrite is stable from the second write on.
        var first = OxceSaveAdapter.EmitLoadedCampaign(restored.Capture(), loaded.Source);
        var again = Load(first, campaign);
        Assert.Equal(first, OxceSaveAdapter.EmitLoadedCampaign(again.Campaign.Capture(), again.Source));
    }

    [Fact]
    public void ASaveHoldingABattleNeedsTheTerrainFiles()
    {
        var (campaign, _) = Started();
        var text = OxceSaveAdapter.EmitNewCampaign(AtFirstTurn(campaign));
        Assert.Equal(CampaignBattles.RestoreNeedsTerrainFiles, Assert.Throws<InvalidDataException>(() => Load(text, campaign, terrain: false)).Message);
    }

    [Fact]
    public void TheBattlescapeFlagsAreSetWhenTheBattleStartsAndRoundTrip()
    {
        // The UFO and the craft that lands at it are moving when the landing is confirmed.
        var (campaign, _) = BattleStartTests.Requested(BattleStartTests.Rules, adjust: static snapshot => (snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [.. snapshot.World.Ufos.Select(static ufo => ufo with { Speed = 300, SpeedRadian = 0.01, SpeedLongitude = 0.02, SpeedLatitude = 0.03 })],
            },
        }).WithCraft("SHIP", static craft => craft with
        {
            Logistics = craft.Logistics! with { Speed = 700, SpeedRadian = 0.04, SpeedLongitude = 0.05, SpeedLatitude = 0.06 },
        }));
        var moving = campaign.Capture();
        Assert.Equal(300, Assert.Single(moving.World.Ufos).Speed);
        Assert.Equal(700, moving.Bases[0].Crafts.Single(static craft => craft.RuleId == "SHIP").Logistics!.Speed);
        BattleStartTests.Started(campaign);
        // setCraft and setUfo: the landing craft and its UFO are in the battlescape and stand still (setInBattlescape(true)
        // calls setSpeed(0), which clears the speed vector); the base is not in the battlescape.
        var snapshot = AtFirstTurn(campaign);
        var ufo = Assert.Single(snapshot.World.Ufos);
        Assert.Equal((true, 0, 0.0, 0.0, 0.0), (ufo.InBattlescape, ufo.Speed, ufo.SpeedRadian, ufo.SpeedLongitude, ufo.SpeedLatitude));
        var ship = snapshot.Bases[0].Crafts.Single(static craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal((true, 0, 0.0, 0.0, 0.0), (ship.InBattlescape, ship.Speed, ship.SpeedRadian, ship.SpeedLongitude, ship.SpeedLatitude));
        Assert.False(snapshot.Bases[0].InBattlescape);
        // Base::save writes inBattlescape only when set. Craft::load stops a craft in the battlescape (setSpeed(0)) even when
        // a save (not one the reference writes) gives it a speed.
        snapshot = snapshot with { Bases = [snapshot.Bases[0] with { InBattlescape = true }, .. snapshot.Bases.Skip(1)] };
        snapshot = snapshot.WithCraft("SHIP", static craft => craft with
        {
            Logistics = craft.Logistics! with { Speed = 500, SpeedLongitude = 0.1, SpeedLatitude = 0.2, SpeedRadian = 0.3 },
        });
        var text = OxceSaveAdapter.EmitNewCampaign(snapshot);
        var body = Document(text, 1);
        Assert.True(Mappings(body, "bases")[0].ReadBoolean("inBattlescape", false));
        Assert.True(Craft(body).ReadBoolean("inBattlescape", false));
        Assert.Equal(500, Craft(body).ReadInt32("speed", 0));
        Assert.True(Assert.Single(Mappings(body, "ufos")).ReadBoolean("inBattlescape", false));

        var reloaded = Load(text, campaign).Campaign.Capture();
        Assert.True(reloaded.Bases[0].InBattlescape);
        Assert.True(Assert.Single(reloaded.World.Ufos).InBattlescape);
        var craft = reloaded.Bases[0].Crafts.Single(static craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal((true, 0, 0.0, 0.0, 0.0), (craft.InBattlescape, craft.Speed, craft.SpeedLongitude, craft.SpeedLatitude, craft.SpeedRadian));
        // A base and a craft not in the battlescape write no flag.
        var calm = snapshot with { Bases = [snapshot.Bases[0] with { InBattlescape = false }, .. snapshot.Bases.Skip(1)] };
        var plain = Document(OxceSaveAdapter.EmitNewCampaign(calm.WithCraft("SHIP", static craft => craft with
        {
            Logistics = craft.Logistics! with { InBattlescape = false },
        })), 1);
        Assert.False(Mappings(plain, "bases")[0].TryGet("inBattlescape", out _));
        Assert.False(Craft(plain).TryGet("inBattlescape", out _));
    }

    [Theory]
    [InlineData("UFO_SUITED", true)]
    [InlineData("UFO_REPLACED", false)]
    public void TheSoldiersBattleArmorRoundTripsWithTheArmorItReplaced(string deployment, bool transformed)
    {
        var (campaign, battle) = Started(deployment);
        var soldierId = Assert.Single(battle.Units, static unit => unit.Faction == BattleFaction.Player).SoldierId!.Value;
        var text = OxceSaveAdapter.EmitNewCampaign(AtFirstTurn(campaign));
        var node = Mappings(Mappings(Document(text, 1), "bases")[0], "soldiers").Single(soldier => soldier.ReadInt32("id", 0) == soldierId);
        Assert.Equal(("SUIT", transformed ? "ARMOR" : "", transformed ? "" : "ARMOR"),
            (node.ReadString("armor", ""), node.ReadString("transformedArmor", ""), node.ReadString("replacedArmor", "")));

        // The restore makes the soldier unit in the soldier's armor, the battle armor, and remembers its own.
        var restored = Load(text, campaign).Campaign;
        var unit = Assert.Single(LoadedBattle(restored).Units, unit => unit.SoldierId == soldierId);
        Assert.Equal(("SUIT", "ARMOR"), (unit.ArmorId, unit.OriginalArmorId));
        var personal = restored.Capture().Bases[0].Soldiers.Single(soldier => soldier.Id == soldierId).Personal!;
        Assert.Equal(("SUIT", transformed ? "ARMOR" : "", transformed ? "" : "ARMOR"),
            (personal.Armor, personal.TransformedArmor, personal.ReplacedArmor));
    }

    [Fact]
    public void AFailedStartLeavesTheCampaignsFlagsAndArmorAlone()
    {
        // The suited deployment's aliens have no item set, so generation fails in deployAliens, after deployXCOM's armor
        // pass changed the soldier's armor (the reference throws there); the refusal changes nothing on the campaign.
        var rules = BattleStartTests.Rules.Replace("    enviroEffects: ENV_SUIT\n    data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]\n",
            "    enviroEffects: ENV_SUIT\n    data: [{customUnitType: ALIEN, lowQty: 1, itemSets: []}]\n", StringComparison.Ordinal);
        Assert.NotEqual(BattleStartTests.Rules, rules);
        var (campaign, _) = BattleStartTests.Requested(rules, "UFO_SUITED");
        var before = campaign.Capture();
        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new StartBattle()).Events));
        Assert.Equal((CampaignBattles.GenerationFailedCode, "Unit generator encountered an error: item set not defined"),
            (blocked.Code, blocked.Reason));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.All(campaign.Capture().Bases[0].Soldiers, static soldier => Assert.Equal("", soldier.Personal!.TransformedArmor));
    }

    [Fact]
    public void ATransformationIntoTheSameArmorIsRemembered()
    {
        // getArmorTransformation returns the same armor: deployXCOM still calls setTransformedArmor and setArmor.
        var rules = BattleStartTests.Rules.Replace("armorTransformations: {ARMOR: SUIT}", "armorTransformations: {ARMOR: ARMOR}",
            StringComparison.Ordinal);
        Assert.NotEqual(BattleStartTests.Rules, rules);
        var (campaign, request) = BattleStartTests.Requested(rules, "UFO_SUITED");
        BattleStartTests.Started(campaign);
        var personal = campaign.Capture().Bases[0].Soldiers.Single(soldier => soldier.Id == Assert.Single(request.Soldiers)).Personal!;
        Assert.Equal(("ARMOR", "ARMOR", ""), (personal.Armor, personal.TransformedArmor, personal.ReplacedArmor));
        var unit = Assert.Single(BattleStartTests.Battle(campaign).Units, static unit => unit.Faction == BattleFaction.Player);
        Assert.Equal(("ARMOR", "ARMOR"), (unit.ArmorId, unit.OriginalArmorId));
    }

    [Fact]
    public void AReplacementIntoABiggerArmorIsDropped()
    {
        // "cannot switch into a bigger armor size!": the soldier keeps its armor and nothing is remembered.
        var rules = BattleStartTests.Rules.Replace("defaultArmor: {RECRUIT: {SUIT: 2}}", "defaultArmor: {RECRUIT: {VEHICLE_ARMOR: 2}}",
            StringComparison.Ordinal);
        Assert.NotEqual(BattleStartTests.Rules, rules);
        var (campaign, request) = BattleStartTests.Requested(rules, "UFO_REPLACED");
        var before = campaign.Capture().Bases[0].Soldiers.Single(soldier => soldier.Id == Assert.Single(request.Soldiers)).Personal!;
        BattleStartTests.Started(campaign);
        var personal = campaign.Capture().Bases[0].Soldiers.Single(soldier => soldier.Id == Assert.Single(request.Soldiers)).Personal!;
        Assert.Equivalent(before, personal, strict: true);
        Assert.Equal(("ARMOR", "", ""), (personal.Armor, personal.TransformedArmor, personal.ReplacedArmor));
        var unit = Assert.Single(BattleStartTests.Battle(campaign).Units, static unit => unit.Faction == BattleFaction.Player);
        Assert.Equal(("ARMOR", ""), (unit.ArmorId, unit.OriginalArmorId));
    }

    [Fact]
    public void LoadedRewritesKeepUnknownBattleKeys()
    {
        var (campaign, _) = Started();
        var text = OxceSaveAdapter.EmitNewCampaign(AtFirstTurn(campaign));
        var marked = text.Replace("\nbattleGame:\n", "\nbattleGame:\n  oxceUnknownBattleKey: 7\n", StringComparison.Ordinal);
        Assert.NotEqual(text, marked);
        var loaded = Load(marked, campaign);
        var rewritten = Document(OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source), 1);
        Assert.True(rewritten.TryGet("battleGame", out var battle));
        Assert.Equal(7, ((YamlMappingNode)battle!).ReadInt32("oxceUnknownBattleKey", 0));
    }

    [Fact]
    public void WithoutABattleTheHeadersBattleLinesAreDroppedAndABattleGameThatIsNoMappingFails()
    {
        var (campaign, _) = BattleStartTests.Requested(BattleStartTests.Rules);
        var text = OxceSaveAdapter.EmitNewCampaign(campaign.Capture());
        Assert.False(Document(text, 0).TryGet("turn", out _));
        Assert.False(Document(text, 1).TryGet("battleGame", out _));
        // A header with battle lines but no battleGame: no battle, and a rewrite drops the lines.
        var stale = "mission: STR_UFO_CRASH_RECOVERY\nturn: 3\n" + text;
        var loaded = Load(stale, campaign, terrain: false);
        Assert.Null(loaded.Campaign.Capture().Battle);
        var rewritten = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
        Assert.False(Document(rewritten, 0).TryGet("mission", out _));
        Assert.False(Document(rewritten, 0).TryGet("turn", out _));
        Assert.False(Document(rewritten, 1).TryGet("battleGame", out _));
        // A battleGame that is no mapping (a null node, a sequence, a scalar) fails the load, as its missing selectedUnit
        // fails SavedBattleGame::load.
        foreach (var value in new[] { "~", "[1]", "7" })
            Assert.Equal("Battle save 'battleGame' is not a mapping.", Assert.Throws<InvalidDataException>(() =>
                Load(text.TrimEnd('\n') + "\nbattleGame: " + value + "\n", campaign)).Message);
    }

    [Fact]
    public void AShadowCopyRestoresTheBattle()
    {
        var (campaign, _) = Started();
        var shadow = campaign.CreateShadow();
        Assert.Equivalent(BattleSaveUnitTests.Loaded(campaign.Capture().Battle!), shadow.Capture().Battle, strict: true);
    }
}
