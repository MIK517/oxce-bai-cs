using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Scripting.Api;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>Branch 4c closure R1: remaining callable detection providers and register-bounded loops.</summary>
public sealed class StrategicWorldDetectionClosureTests
{
    private static readonly string[] LoopGuards =
    [
        "Detection script requires a bounded loop forecast.",
        "Detection script exceeds the supported instruction limit.",
    ];

    private static CampaignTime HalfHour => new(1, 1, 1, 1999, 1, 29, 55);

    [Theory]
    [InlineData(1970, 1, 1, 0, 0)]
    [InlineData(1999, 1, 1, 1, 10592)]
    [InlineData(2000, 3, 1, 23, 11017)]
    [InlineData(1969, 12, 31, 12, 0)] // -43200 s truncates toward zero, as C++ division does
    [InlineData(1969, 12, 31, 0, -1)]
    [InlineData(1900, 1, 1, 0, -25567)]
    public void DaysPastEpochTruncatesUtcMktimeSeconds(int year, int month, int day, int hour, int expected) =>
        Assert.Equal(expected, CampaignTimeScriptValues.DaysPastEpoch(new CampaignTime(1, day, month, year, hour, 0, 0)));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DaysPastEpochReadsTheDetectionTick(bool fromCache, bool fromCraft) =>
        AssertDetectionReadScript("var ptr Time time; var ptr GeoscapeGame absent null; var int value; " +
            "geoscape_game.getTime time; time.getDaysPastEpoch value; if neq value 10592; return DETECTION_NONE 0; end; " +
            "absent.getTime time; time.getDaysPastEpoch value; if neq value 0; return DETECTION_NONE 0; end; " +
            "return DETECTION_RADAR 100;", fromCache, fromCraft);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void LiteralLimitRegistersBoundCountedLoops(bool fromCache, bool fromCraft) =>
        AssertDetectionReadScript("var int count 1; var int runs; var int skipped; loop var i count; add runs 1; end; " +
            "set count 3; loop var j count; add runs 1; end; if neq runs 4; return DETECTION_NONE 0; end; " +
            "loop var k skipped; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
            fromCache, fromCraft);

    [Theory]
    [InlineData("var int count 1; add count 1; loop var i count; add detection_chance 1; end; return;")]
    [InlineData("var int count 1; set count distance; loop var i count; add detection_chance 1; end; return;")]
    [InlineData("loop var i distance; add detection_chance 1; end; return;")]
    [InlineData("var int count 2000000; loop var i count; add detection_chance 1; end; return;")]
    public void ComputedOrOversizedLoopLimitsRemainGuarded(string code)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, $$"""
            ufos:
              - type: UFO_LOOP_GUARD
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: '{{code}}'
            """);
        var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_LOOP_GUARD", 2200);
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Contains(Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason, LoopGuards);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ArmorScriptProvidersReadOverridesDefaultsAndLinks(bool fromCache, bool fromCraft)
    {
        // Armor::getArmor, the remaining Armor::ScriptRegister getters and their defaults
        // (deathFrames 3, move costs 100/100, no corpses or research) and null receivers.
        string Check(string call, int expected) => $"{call}; if neq value {expected}; return DETECTION_NONE 0; end; ";
        string Same(string call, string expected) =>
            $"{call}; rules.getRuleItem expected \"{expected}\"; if neq item expected; return DETECTION_NONE 0; end; ";
        var code = "var ptr RuleArmor armor; var ptr RuleArmor plain; var ptr RuleArmor absent null; var ptr RuleItem item; " +
            "var ptr RuleItem expected; var ptr RuleResearch research; var int value; " +
            "rules.getRuleArmor armor \"ARMOR_SCRIPT_READS\"; rules.getRuleArmor plain \"ARMOR_SCRIPT_DEFAULTS\"; " +
            Check("armor.getArmor value 0", 11) + Check("armor.getArmor value 1", 15) + Check("armor.getArmor value 2", 12) +
            Check("armor.getArmor value 3", 13) + Check("armor.getArmor value 4", 14) + Check("armor.getArmor value 5", 0) +
            Check("armor.getArmor value -1", 0) + Check("absent.getArmor value 0", 0) +
            Check("armor.getDrawingRoutine value", 4) + Check("armor.getDrawBubbles value", 1) +
            Check("armor.getDeathFrames value", 5) + Check("armor.getConstantAnimation value", 1) +
            Check("armor.MoveCost.getBaseTimePercent value", 90) + Check("armor.MoveCost.getBaseEnergyPercent value", 80) +
            Check("armor.MoveCost.getBaseNormalTimePercent value", 70) + Check("armor.MoveCost.getBaseNormalEnergyPercent value", 60) +
            Check("armor.MoveCost.getBaseFlyTimePercent value", 50) + Check("armor.MoveCost.getBaseFlyEnergyPercent value", 40) +
            Check("plain.getDeathFrames value", 3) + Check("plain.getDrawBubbles value", 0) +
            Check("plain.MoveCost.getBaseFlyEnergyPercent value", 100) + Check("absent.getDeathFrames value", 0) +
            Same("armor.getCorpseGeoscape item", "CORPSE_GEO") + Same("armor.getCorpseBattlescape item", "CORPSE_A") +
            Same("armor.getCorpseBattlescape item 1", "CORPSE_B") +
            "armor.getCorpseBattlescape item 4; if neq item null; return DETECTION_NONE 0; end; " +
            "armor.getCorpseBattlescape item -1; if neq item null; return DETECTION_NONE 0; end; " +
            "absent.getCorpseGeoscape item; if neq item null; return DETECTION_NONE 0; end; " +
            "armor.getRequiredResearch research; research.getPoints value; if neq value 7; return DETECTION_NONE 0; end; " +
            "plain.getRequiredResearch research; if neq research null; return DETECTION_NONE 0; end; " +
            "return DETECTION_RADAR 100;";
        AssertDetectionReadScript(code, fromCache, fromCraft, """
            items:
              - type: CORPSE_GEO
              - type: CORPSE_A
              - type: CORPSE_B
            research:
              - name: ARMOR_REQUIRED_TOPIC
                cost: 1
                points: 7
            armors:
              - type: ARMOR_SCRIPT_READS
                size: 2
                corpseBattle: [CORPSE_A, CORPSE_B, CORPSE_A, CORPSE_B]
                corpseGeo: CORPSE_GEO
                loftempsSet: [0, 0, 0, 0]
                requires: ARMOR_REQUIRED_TOPIC
                frontArmor: 11
                sideArmor: 12
                leftArmorDiff: 3
                rearArmor: 13
                underArmor: 14
                drawingRoutine: 4
                drawBubbles: true
                deathFrames: 5
                constantAnimation: true
                moveCost:
                  basePercent: [90, 80]
                  baseNormalPercent: [70, 60]
                  baseFlyPercent: [50, 40]
              - type: ARMOR_SCRIPT_DEFAULTS
                corpseBattle: [CORPSE_A]
                loftempsSet: [0]
            """);
    }

    /// <summary>
    /// Fixed ledger of every detectUfoFromBase/detectUfoFromCraft binding in the pinned
    /// catalogue that the strategic detection pass does not execute, keyed by name and
    /// receiver type, with the reason it stays unavailable. All other declarations run.
    /// The ledger is summarized in docs/strategic-world-operations-status.md.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> UnavailableLedger = BuildLedger();

    [Fact]
    public void DetectionBindingLedgerClassifiesEveryUnavailableProvider()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"]);
        var campaign = CampaignState.Restore(CreateSnapshot(content, "Detection binding ledger"), content, new SplitMix64RandomSource(17));
        var world = Assert.IsType<CampaignWorld>(campaign.GetQuery<ICampaignWorldQuery>());
        var catalog = ReferenceScriptApiCatalog.Instance;
        var bindings = catalog.Bindings
            .Where(static binding => binding.ParserGroups.Contains("detectUfoFromBase") || binding.ParserGroups.Contains("detectUfoFromCraft"))
            .ToArray();
        Assert.Equal(377, bindings.Length);
        var unavailable = bindings.Where(binding => !world.DetectionBindingSupported(binding))
            .Select(binding => $"{binding.Name}/{(catalog.TryGetType(binding.Parameters[0].Type.Id, out var type) ? type!.Name : "?")}")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(UnavailableLedger.Keys.Order(StringComparer.Ordinal), unavailable);
    }

    private static Dictionary<string, string> BuildLedger()
    {
        const string Tactical = "BattleGame/Tile receiver: no detection input provides one (tactical scripting, branch 5)";
        const string Editable = "needs an editable receiver; detection inputs are read-only";
        const string Random = "RandomState only comes from the editable GeoscapeGame.getRandomState";
        const string Resources = "needs loaded vanilla resource-set sizes (Mod::_soundOffset*/_surfaceOffset*), outside headless content";
        const string Position = "Position value arithmetic: the script VM has no Position value representation (tactical scripting, branch 5)";
        const string Target = "receiver is the unregistered Target base class; a Craft/Ufo pointer cannot bind to it";
        var ledger = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string reason, string receiver, params string[] names)
        {
            foreach (var name in names) ledger[$"{name}/{receiver}"] = reason;
        }
        Add(Tactical, "BattleGame", "BattleGame.difficultyLevel", "BattleGame.flashLongMessage", "BattleGame.flashMessage",
            "BattleGame.getAlienItemLevel", "BattleGame.getAnimFrame", "BattleGame.getDepth", "BattleGame.getGeoscapeGame",
            "BattleGame.getGlobalShade", "BattleGame.getItems.list", "BattleGame.getReinforcementsItemLevel", "BattleGame.getSize.getX",
            "BattleGame.getSize.getY", "BattleGame.getSize.getZ", "BattleGame.getTag", "BattleGame.getTile", "BattleGame.getTurn",
            "BattleGame.getTurnSide", "BattleGame.getUnits.byFaction.list", "BattleGame.getUnits.list", "BattleGame.randomChance",
            "BattleGame.randomRange", "BattleGame.setAlienItemLevel", "BattleGame.setReinforcementsItemLevel", "BattleGame.setTag",
            "BattleGame.tryConcealUnit");
        Add(Tactical, "Tile", "Tile.getDistanceTile", "Tile.getDistanceVoxel", "Tile.getFire", "Tile.getFloorSpecialTileType",
            "Tile.getObjectSpecialTileType", "Tile.getPosition.getX", "Tile.getPosition.getY", "Tile.getPosition.getZ", "Tile.getPosition",
            "Tile.getShade", "Tile.getSmoke", "Tile.getUnit", "Tile.isDiscovered", "Tile.makeDiscovered");
        Add(Editable, "Country", "Country.setTag");
        Add(Editable, "Craft", "Craft.setTag");
        Add(Editable, "GeoscapeGame", "GeoscapeGame.setTag", "GeoscapeGame.getRandomState");
        Add(Editable, "RuleCraft", "RuleCraft.setTag");
        Add(Editable, "RuleMod", "RuleMod.setTag");
        Add(Editable, "RuleSkill", "RuleSkill.setTag");
        Add(Editable, "Ufo", "Ufo.setTag");
        Add(Random, "RandomState", "RandomState.randomChance", "RandomState.randomRange", "RandomState.randomRangeSymmetric");
        Add(Resources, "RuleMod", "RuleMod.getSoundOffsetBattle", "RuleMod.getSoundOffsetGeo", "RuleMod.getSpriteOffsetBasebits",
            "RuleMod.getSpriteOffsetBigobs", "RuleMod.getSpriteOffsetFloorob", "RuleMod.getSpriteOffsetHandob",
            "RuleMod.getSpriteOffsetHit", "RuleMod.getSpriteOffsetSmoke");
        Add(Position, "Position", "add", "sub", "mul", "muldiv", "set", "Position.addX", "Position.addY", "Position.addZ",
            "Position.getX", "Position.getY", "Position.getZ", "Position.setX", "Position.setY", "Position.setZ");
        Add(Target, "null", "Craft.getId", "Ufo.getId");
        return ledger;
    }
}
