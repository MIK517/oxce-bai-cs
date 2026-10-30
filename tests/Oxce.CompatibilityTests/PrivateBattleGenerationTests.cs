using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Bootstrap;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 1 S7 acceptance: deployment requests of the staged installation's saves generate battles headlessly
/// through <c>StartBattle</c>, with the installation's own terrain files. Every battle passes
/// <see cref="BattleState.Validate"/> and its fixed-seed structure is pinned: terrain, map script, size, the blocks per
/// 10x10 cell, units per faction (all placed) and route nodes. The Rosigma <c>Early.sav</c> terror landing is the
/// first acceptance scenario; the vanilla UFO and TFTD landings are staged by a headless player that sends a manned
/// craft to the first landed or crashed UFO or mission site it sees, and the base defenses by a retaliation UFO staged
/// to reach the save's first base. Malformed terrain files refuse the battle with the file's path and leave the request pending.
/// Phase 7 branch 2 S6: each acceptance battle, written by the battle save module, reloads with the same saved and derived
/// state and the same viewer overviews.
/// </summary>
public sealed class PrivateBattleGenerationTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // The loaded save each campaign came from, which a loaded rewrite writes over.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CampaignState, OxceSaveDocument> Sources = [];

    // Real mods run with the production forecast work bounds (branch 6 S3).
    public PrivateBattleGenerationTests() => CampaignWorld.ProductionWorkBounds = true;

    private static readonly (string Master, string AddOn, string Family)[] Families =
    [
        ("xcom1", "-", "vanilla-ufo"), ("xcom2", "-", "vanilla-tftd"), ("40k", "40k_ROSIGMA_edits", "modded/rosigma"),
    ];

    /// <summary>The fixed-seed structure of a generated battle; <c>Blocks</c> lists the block of each 10x10 cell, row by row.</summary>
    private sealed record BattleShape(string Request, string Terrain, string Script, string Size, string Blocks, int Player,
        int Hostile, int Neutral, int Nodes);

    [Fact]
    public void RosigmaEarlyTerrorLandingGeneratesTheFirstAcceptanceBattle()
    {
        var installation = TestFixtures.RequirePrivateInstallation();
        WithFamily(installation, "modded/rosigma", loaded =>
        {
            var campaign = LoadSave(loaded, "modded/rosigma", "Early.sav");
            var (ticks, request, _) = RunToDeployment(campaign, dispatch: false);
            Assert.Equal(6290, ticks);
            Assert.NotNull(request);
            var battle = Start(campaign);
            Assert.Equal(new BattleShape("STR_TERROR_MISSION/STR_TERROR_MISSION/STR_SLAANESH_CULT_EARLY/MissionSite",
                "INDUSTRIALURBANDIO", "INDUSTRIALURBANDIO", "60x60x8",
                "INDUSTRIALURBANCULTROADRAILNS,IMPULSOR,INDUSTRIALURBANCULT34B,INDUSTRIALURBANCULT14,INDUSTRIALURBANCULT14,INDUSTRIALURBANCULT32E," +
                "INDUSTRIALURBANCULTROADRAILNS,IMPULSOR,INDUSTRIALURBANCULT05,INDUSTRIALURBANCULT30C,INDUSTRIALURBANCULT15,INDUSTRIALURBANCULT34," +
                "INDUSTRIALURBANCULTROADRAILNS,INDUSTRIALURBANCULT33B,INDUSTRIALURBANCULT12B,INDUSTRIALURBANCULT34,INDUSTRIALURBANCULT21,INDUSTRIALURBANCULT10A," +
                "INDUSTRIALURBANCULTROADRAILNS,INDUSTRIALURBANCULT33F,INDUSTRIALURBANCULT14,INDUSTRIALURBANCULTDIO19,INDUSTRIALURBANCULT32E,INDUSTRIALURBANCULT15," +
                "INDUSTRIALURBANCULTROADRAILNS,INDUSTRIALURBANCULT30C,INDUSTRIALURBANCULT14A,INDUSTRIALURBANCULT08,INDUSTRIALURBANCULT33,INDUSTRIALURBANCULT33F," +
                "INDUSTRIALURBANCULTROADRAILNS,INDUSTRIALURBANCULT09,INDUSTRIALURBANCULT34,INDUSTRIALURBANCULT09,INDUSTRIALURBANCULT04,INDUSTRIALURBANCULT10A",
                10, 22, 10, 397), Shape(request!, battle));
            // The ten soldiers on board, each of them on the map; the player sees its own side only.
            Assert.Equal(request!.Soldiers.Order(), battle.Units.Where(static unit => unit.SoldierId is not null).Select(static unit => unit.SoldierId!.Value).Order());
            Assert.Equal(10, campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer)!.Units.Count);
            // The campaign holds for the battle, and saving waits for the first turn (Phase 7 branch 3).
            Assert.Null(campaign.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest());
            Assert.Equal(CampaignBattles.BattleInProgress.Text, Blocked(campaign.Execute(new AdvanceCampaignTime(1))));
            Assert.Equal(CampaignBattles.BattleSaveBeforeFirstTurn.Text, campaign.QuerySaveRefusal());
            AssertReloads(loaded, campaign, battle);
        });
    }

    [Fact]
    public void StagedLandingsGenerateValidatedBattlesWithPinnedStructure() => AssertShapes(new()
    {
        // A farm landing: BARN object 7 has tile type 10, which nothing turns into (S7).
        [("vanilla-ufo", "Begining.sav")] = new("STR_UFO_GROUND_ASSAULT/STR_MEDIUM_SCOUT/STR_SECTOID/Ufo", "CULTA", "FARM", "40x40x4",
            "CULTA09,CULTA04,CULTA07,CULTA02,CULTA03,CULTA08,CULTA10,CULTA04,CULTA15,UFO_110,PLANE,CULTA11,CULTA13,CULTA18,PLANE,CULTA01",
            8, 4, 0, 56),
        [("vanilla-tftd", "Begining.sav")] = new("STR_ISLAND_TERROR/STR_ISLAND_TERROR/STR_GILLMAN/MissionSite", "ISLAND", "ISLAND_TERROR", "50x50x4",
            "ISLAND06,ISLAND06,ISLAND12,ISLAND08,ISLAND08,ISLAND06,ISLAND06,TRITON,ISLAND02,ISLAND02,ISLAND14,ISLAND14,TRITON,ISLAND02,ISLAND02," +
            "ISLAND10,ISLAND04,ISLAND04,ISLAND05,ISLAND05,ISLAND07,ISLAND04,ISLAND04,ISLAND05,ISLAND05", 8, 21, 12, 97),
        [("vanilla-tftd", "IronMode.sav")] = new("STR_UFO_GROUND_ASSAULT/STR_ESCORT/STR_GILLMAN/Ufo", "MSUNK", "THREEBIG", "50x50x4",
            "MSUNK12,MSUNK13,MSUNK07,MSUNK02,MSUNK02,MSUNK04,MSUNK04,TRITON,MSUNK02,MSUNK02,MSUNK04,MSUNK04,TRITON,MSUNK10,MSUNK10," +
            "UFO02,MSUNK07,MSUNK10,MSUNK05,MSUNK05,MSUNK13,MSUNK11,MSUNK13,MSUNK05,MSUNK05", 8, 7, 0, 141),
        [("vanilla-tftd", "Middle.sav")] = new("STR_PORT_TERROR/STR_PORT_TERROR/STR_GILLMAN/MissionSite", "PORT", "PORT_TERROR", "50x50x4",
            "PORT18,PORT18,PORT20,PORT20,PORT09,PORT18,PORT18,PORT20,PORT20,PORT03,PORT13,PORT17,PORT17,TRITON,PORT08," +
            "PORT11,PORT17,PORT17,TRITON,PORT02,PORT15,PORT11,PORT15,PORT12,PORT07", 2, 20, 16, 169),
        [("modded/rosigma", "Middle.sav")] = new("STR_UFO_GROUND_ASSAULT/STR_LARGE_SCOUT_BRIDGED_TOWERS/STR_TZEENTCH_MUTANT_CULT/Ufo",
            "PLAINSSAND", "PLAINS", "40x40x9",
            "PLAINS05,PLAINS07,PLAINS08,IMPULSOR,CH02BRIDGEDTOWERS,CH02BRIDGEDTOWERS,PLAINS08,IMPULSOR," +
            "CH02BRIDGEDTOWERS,CH02BRIDGEDTOWERS,PLAINS11,PLAINS11,PLAINS01,PLAINS06,PLAINS11,PLAINS11", 9, 16, 0, 76),
    }, "private-battle-landings.json", (loaded, family, save) =>
    {
        var campaign = LoadSave(loaded, family, save);
        var (_, request, stop) = RunToDeployment(campaign, dispatch: true);
        Assert.True(request is not null, $"{family}/{save} stopped without a deployment request: {stop}");
        return (campaign, request);
    });

    [Fact]
    public void StagedBaseDefensesGenerateValidatedBattles() => AssertShapes(new()
    {
        [("vanilla-ufo", "Middle.sav")] = new("STR_BASE_DEFENSE//STR_SECTOID/Base", "XBASE", "XBASE", "60x60x2",
            "XBASE_16,XBASE_17,XBASE_16,XBASE_17,XBASE_16,XBASE_17,XBASE_18,XBASE_19,XBASE_18,XBASE_19,XBASE_18,XBASE_19,XBASE_00,XBASE_20,XBASE_20,XBASE_20,XBASE_20,XBASE_20," +
            "XBASE_01,XBASE_07,XBASE_02,XBASE_03,XBASE_04,XBASE_20,XBASE_20,XBASE_20,XBASE_20,XBASE_20,XBASE_20,XBASE_20,XBASE_20,XBASE_20,XBASE_20,XBASE_20,XBASE_20,XBASE_20", 2, 16, 0, 111),
        [("vanilla-tftd", "IronMode.sav")] = new("STR_BASE_DEFENSE//STR_AQUATOID/Base", "XBASES", "BASE_DRILL", "60x60x4",
            "XBASES16,XBASES17,XBASES16,XBASES17,XBASES16,XBASES17,XBASES18,XBASES19,XBASES18,XBASES19,XBASES18,XBASES19,XBASES20,XBASES20,XBASES00,XBASES20,XBASES20,XBASES20," +
            "XBASES20,XBASES03,XBASES01,XBASES07,XBASES02,XBASES04,XBASES20,XBASES20,XBASES20,XBASES20,XBASES20,XBASES20,XBASES20,XBASES20,XBASES20,XBASES20,XBASES20,XBASES20", 8, 25, 0, 132),
        [("modded/rosigma", "Early.sav")] = new("STR_BASE_DEFENSE//STR_TZEENTCH_MUTANT_CULT/Base", "XBASE", "XBASE", "60x60x4",
            "XBASE_16,XBASE_17,XBASE_16,XBASE_17,XBASE_16,XBASE_17,XBASE_18,XBASE_19,XBASE_18,XBASE_19,XBASE_18,XBASE_19,XBASE_16,XBASE_17,XBASE_00,XBASE_20,XBASE_20,XBASE_20," +
            "XBASE_18,XBASE_19,XBASE_01,XBASE_07,XBASE_20,XBASE_20,XBASE_20,XBASE_20,XBASE_02,XBASE_03,XBASE_20,XBASE_20,XBASE_20,XBASE_20,XBASE_04,XBASE_20,XBASE_20,XBASE_20", 9, 31, 0, 616),
    }, "private-battle-base-defenses.json", (loaded, family, save) =>
    {
        var (campaign, request, events) = StageBaseAttack(loaded, LoadSave(loaded, family, save));
        Assert.True(request is not null, $"{family}/{save}: {string.Join(", ", events)}");
        return (campaign, request);
    });

    /// <summary>
    /// handleBaseDefense's gate: TFTD <c>Middle.sav</c>'s two soldiers are on the Triton, which is out, and the base has no
    /// vehicles, so the attack destroys the base (BaseDestroyedState) and no battle starts.
    /// </summary>
    [Fact]
    public void AnUndefendedBaseIsDestroyedWithoutABattle()
    {
        var installation = TestFixtures.RequirePrivateInstallation();
        WithFamily(installation, "vanilla-tftd", loaded =>
        {
            var (campaign, request, events) = StageBaseAttack(loaded, LoadSave(loaded, "vanilla-tftd", "Middle.sav"));
            Assert.Null(request);
            Assert.Contains(events, static evt => evt is BaseDestroyed);
            Assert.Null(campaign.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest());
        });
    }



    [Fact]
    public void MalformedTerrainFilesRefuseTheBattleWithTheirPathAndLeaveTheRequestPending()
    {
        var installation = TestFixtures.RequirePrivateInstallation();
        WithFamily(installation, "modded/rosigma", loaded =>
        {
            // The files a good generation reads: a map block's MAP and RMP and a terrain data set's MCD.
            var good = LoadSave(loaded, "modded/rosigma", "Early.sav");
            RunToDeployment(good, dispatch: false);
            var reads = new RecordingFiles(loaded.Files);
            good.BattleTerrainFiles = reads;
            Start(good);
            var map = reads.Paths.First(static path => path.StartsWith("MAPS/", StringComparison.Ordinal));
            var block = map["MAPS/".Length..^".MAP".Length];
            var dataSet = reads.Paths.First(static path => path.StartsWith("TERRAIN/", StringComparison.Ordinal));
            foreach (var (path, change, reason) in new (string, Func<byte[], byte[]?>, string)[]
            {
                ($"ROUTES/{block}.RMP", static _ => null, $"Terrain file ROUTES/{block}.RMP is missing."),
                (dataSet, static _ => null, $"Terrain file {dataSet} is missing."),
                // A whole MAP twice as long as its block (the header is length, width, height), and one cut after its header.
                (map, static bytes => [(byte)(bytes[0] * 2), bytes[1], bytes[2], .. bytes.AsSpan(3), .. bytes.AsSpan(3)],
                    $"Map block is not of the size specified {map} is "),
                (map, static bytes => bytes[..3], $"Invalid MAP file: {map}:"),
            })
            {
                var campaign = LoadSave(loaded, "modded/rosigma", "Early.sav");
                RunToDeployment(campaign, dispatch: false);
                campaign.BattleTerrainFiles = new AlteredFiles(loaded.Files, path, change);
                var before = campaign.Capture();
                var events = campaign.Execute(new StartBattle()).Events;
                Assert.True(events is [CampaignActionBlocked], $"{path} did not refuse the battle ({reason}).");
                var blocked = (CampaignActionBlocked)events[0];
                Assert.Equal(CampaignBattles.GenerationFailedCode, blocked.Code);
                Assert.StartsWith(reason, blocked.Reason, StringComparison.Ordinal);
                Assert.Equivalent(before, campaign.Capture(), strict: true);
                Assert.NotNull(campaign.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest());
            }
        });
    }

    private sealed class RecordingFiles(IBattleTerrainFiles inner) : IBattleTerrainFiles
    {
        public List<string> Paths { get; } = [];

        public ReadOnlyMemory<byte>? Read(string virtualPath)
        {
            Paths.Add(virtualPath);
            return inner.Read(virtualPath);
        }
    }

    private sealed class AlteredFiles(IBattleTerrainFiles inner, string path, Func<byte[], byte[]?> change) : IBattleTerrainFiles
    {
        public ReadOnlyMemory<byte>? Read(string virtualPath) => virtualPath != path ? inner.Read(virtualPath)
            : inner.Read(virtualPath) is { } data && change(data.ToArray()) is { } changed ? changed : (ReadOnlyMemory<byte>?)null;
    }

    private sealed record LoadedFamily(RuntimeContent Content, OxceSaveLoadOptions Options, IBattleTerrainFiles Files);

    private static void WithFamily(string installation, string family, Action<LoadedFamily> test)
    {
        var (master, addOn, _) = Families.Single(candidate => candidate.Family == family);
        var cacheDirectory = Directory.CreateTempSubdirectory("oxce-private-battle-cache-");
        try
        {
            var request = InstallationLoadRequest.ForMasterAndAddOn(installation, master, addOn, new("Extended", "8.6.1.0"));
            var loaded = InstallationContentLoader.Load(request, new() { Cache = new() { DirectoryPath = cacheDirectory.FullName } },
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(loaded.IsSuccess, loaded.DescribeFailure());
            var options = new OxceSaveLoadOptions(request.MasterId, request.ActiveMods.ToHashSet(StringComparer.Ordinal),
                new Oxce.Formats.Yaml.YamlReadOptions { MaxBytes = 32 * 1024 * 1024, MaxNodes = 2_000_000 });
            test(new LoadedFamily(loaded.Content!, options, new VirtualBattleTerrainFiles(loaded.VirtualFiles!)));
        }
        finally { cacheDirectory.Delete(recursive: true); }
    }

    private static CampaignState LoadSave(LoadedFamily loaded, string family, string save)
    {
        var file = TestFixtures.RepositoryPath("fixtures", "private", "saves", family, "early", save);
        var load = OxceSaveAdapter.LoadFile(file, loaded.Content, new SplitMix64RandomSource(17), loaded.Options);
        var campaign = load.Campaign;
        campaign.BattleTerrainFiles = loaded.Files;
        Sources.Add(campaign, load.Source);
        return campaign;
    }

    /// <summary>
    /// Phase 7 branch 2 S6 acceptance: the generated battle, at the first turn <c>startFirstTurn</c> would give it (the
    /// campaign saves no battle before it), written by the battle save module through the campaign's loaded rewrite,
    /// passes the conformance reader and reloads with the same saved state (as the load sets it: resetUnitTiles'
    /// visibility), the same derived deployment and the same overview for every viewer. <see cref="BattleOverview.Revision"/>
    /// is the port's change counter, which a restore counts from its own changes (the plan's "State the reference does not
    /// save"), so both sides set it to 0. Equivalence ignores the order of collections, so the module's writes of both
    /// snapshots are compared as text too (the order of units, items, nodes and every per-side or per-index list).
    /// </summary>
    private static void AssertReloads(LoadedFamily loaded, CampaignState campaign, BattleState battle)
    {
        var snapshot = campaign.Capture();
        snapshot = snapshot with { Battle = snapshot.Battle! with { Turn = 1 } };
        var text = OxceSaveAdapter.EmitLoadedCampaign(snapshot, Sources.TryGetValue(campaign, out var source) ? source
            : throw new InvalidOperationException("The campaign was not loaded by LoadSave."));
        BattleSaveConformance.CheckSave(text);
        var reloaded = OxceSaveAdapter.Load(text, "battle.sav", loaded.Content, new SplitMix64RandomSource(17),
            loaded.Options with { BattleTerrainFiles = loaded.Files }).Campaign;
        var expected = WithoutUnsavedSlotPositions(BattleSaveUnitTests.Loaded(snapshot.Battle!));
        var actual = reloaded.Capture().Battle!;
        Assert.Equivalent(expected, actual, strict: true);
        Assert.Equal(BattleSaveRootTests.Emit(OxceBattleSave.Write(expected)), BattleSaveRootTests.Emit(OxceBattleSave.Write(actual)));
        var restored = BattleStartTests.Battle(reloaded);
        Assert.Equal(battle.DeploymentId, restored.DeploymentId);
        foreach (var faction in Enum.GetValues<BattleFaction>())
        {
            var viewer = new BattleViewer(faction);
            var before = battle.Overview(viewer);
            var after = restored.Overview(viewer);
            Assert.Equivalent(before with { Turn = 1, Revision = 0 }, after with { Revision = 0 }, strict: true);
            Assert.Equal(before.Units, after.Units);
        }
        Assert.Null(reloaded.QuerySaveRefusal());
    }

    /// <summary>
    /// A snapshot without the inventory positions the reference does not save: <c>BattleItem::save</c> writes
    /// <c>inventoryX</c> and <c>inventoryY</c> only in a grid slot ("for hands and ground it can be `0` for both"), so a
    /// load leaves the constructor's 0 for every item outside a grid slot (in a hand, on the ground, in no section), where
    /// the generator's layout gave a hand item its layout position.
    /// </summary>
    private static BattleSnapshot WithoutUnsavedSlotPositions(BattleSnapshot snapshot)
    {
        static IReadOnlyList<BattleItemSnapshot> Items(IReadOnlyList<BattleItemSnapshot> items) =>
            [.. items.Select(static item => item.SlotIsGrid ? item : item with { SlotX = 0, SlotY = 0 })];
        return snapshot with
        {
            Items = Items(snapshot.Items),
            ItemsSpecial = Items(snapshot.ItemsSpecial),
            RecoverConditional = Items(snapshot.RecoverConditional),
            RecoverGuaranteed = Items(snapshot.RecoverGuaranteed),
        };
    }

    /// <summary>
    /// Generates each save's battle, records the measured shapes (for updating the expectations when a slice moves them)
    /// and compares them with the expected ones.
    /// </summary>
    private static void AssertShapes(Dictionary<(string Family, string Save), BattleShape> expected, string record,
        Func<LoadedFamily, string, string, (CampaignState Campaign, CampaignDeploymentRequest Request)> stage)
    {
        var installation = TestFixtures.RequirePrivateInstallation();
        var measured = new Dictionary<(string Family, string Save), BattleShape>();
        foreach (var family in expected.Keys.Select(static key => key.Family).Distinct())
            WithFamily(installation, family, loaded =>
            {
                foreach (var save in expected.Keys.Where(key => key.Family == family).Select(static key => key.Save))
                {
                    var (campaign, request) = stage(loaded, family, save);
                    var battle = Start(campaign);
                    measured[(family, save)] = Shape(request, battle);
                    AssertReloads(loaded, campaign, battle);
                }
            });
        var path = TestFixtures.RepositoryPath("artifacts", record);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(measured.OrderBy(static pair => pair.Key)
            .Select(static pair => new { pair.Key.Family, pair.Key.Save, Shape = pair.Value }), Indented));
        Assert.Equal(expected.OrderBy(static pair => pair.Key).ToArray(), measured.OrderBy(static pair => pair.Key).ToArray());
    }

    /// <summary>Starts the pending request's battle, which must not be refused, and checks it again.</summary>
    private static BattleState Start(CampaignState campaign)
    {
        var events = campaign.Execute(new StartBattle()).Events;
        Assert.Equal("", events.OfType<CampaignActionBlocked>().SingleOrDefault()?.Reason ?? "");
        Assert.IsType<BattleStarted>(Assert.Single(events));
        var battle = ((CampaignBattles)campaign.GetQuery<IBattleQuery>()!).Battle!;
        battle.Validate();
        // Every unit the generator kept is on the map, except a base defense's dummy that cannot stand on the first tile.
        Assert.All(battle.Units, static unit => Assert.True(unit.Position is not null || unit.SummonedPlayerUnit, $"Unit {unit.Id} is off the map."));
        return battle;
    }

    private static BattleShape Shape(CampaignDeploymentRequest request, BattleState battle)
    {
        var blocks = new List<string>();
        for (var y = 0; y < battle.Length / 10; y++)
            for (var x = 0; x < battle.Width / 10; x++) blocks.Add(battle.Map.BlockName(x, y));
        int Count(BattleFaction faction) => battle.Units.Count(unit => unit.Faction == faction);
        return new BattleShape($"{request.MissionType}/{request.DeploymentId}/{request.Race}/{request.Target.Kind}", battle.TerrainId,
            battle.MapScriptId, $"{battle.Width}x{battle.Length}x{battle.Height}", string.Join(",", blocks), Count(BattleFaction.Player),
            Count(BattleFaction.Hostile), Count(BattleFaction.Neutral), battle.Map.Nodes.Count);
    }

    private static string Blocked(CampaignCommandResult result) =>
        Assert.Single(result.Events.OfType<CampaignActionBlocked>()).Reason;

    /// <summary>
    /// A base attack staged on the save's first base: once the world has begun, a retaliation UFO of the content's first
    /// retaliation mission without missiles (its first wave's UFO, the save's first mission race) flies the retaliation assault run into
    /// the base, so <c>GeoscapeState::handleBaseDefense</c> raises the request itself (holding fire if the base has
    /// defenses). Returns the request, or null with the attack's events when no battle starts.
    /// </summary>
    private static (CampaignState Campaign, CampaignDeploymentRequest? Request, IReadOnlyList<ICampaignEvent> Events) StageBaseAttack(
        LoadedFamily loaded, CampaignState campaign)
    {
        // A new game's month counter is -1 until the world begins (GeoscapeState::init).
        Assert.Empty(campaign.Execute(new EnterCampaignWorld()).Events.OfType<CampaignActionBlocked>());
        var rules = loaded.Content.RuntimeRules;
        var snapshot = campaign.Capture();
        var owner = snapshot.Bases[0];
        // A UFO that lands its troops: one with missiles damages or destroys the base instead.
        var mission = rules.AlienMissions.Rules.First(rule => rule.Value.Objective == RuntimeMissionObjective.Retaliation &&
            rules.Ufos.GetRequiredValue(rule.Value.Waves[0].UfoType).MissilePower == 0);
        var race = snapshot.World.Missions.Count != 0 ? snapshot.World.Missions[0].Race : rules.AlienRaces.Rules[0].Id;
        // The mission's region needs the zones the assault run uses; the UFO is already at the base.
        var run = rules.UfoTrajectories[rules.UfoTrajectories.GetRequired(RuntimeUfoTrajectoryRule.RetaliationAssaultRun)].Value;
        var zone = run.Waypoints.Max(static waypoint => waypoint.Zone);
        var region = rules.Regions.Rules.First(candidate => candidate.Value.MissionZones.Count > zone).Id;
        var missionId = snapshot.World.Missions.Select(static candidate => candidate.Id).DefaultIfEmpty(0).Max() + 1;
        var ufo = new UfoSnapshot(snapshot.World.Ufos.Select(static candidate => candidate.UniqueId).DefaultIfEmpty(0).Max() + 1,
            mission.Value.Waves[0].UfoType, missionId, RuntimeUfoTrajectoryRule.RetaliationAssaultRun, 0, owner.Longitude - 0.0001,
            owner.Latitude, UfoStatus.Flying, WorldAltitudes.High)
        {
            Id = snapshot.World.Ufos.Select(static candidate => candidate.Id).DefaultIfEmpty(0).Max() + 1,
            Speed = 1000,
            MissionWaveNumber = 0,
            Shield = 0,
            Destination = WorldTargetReference.ForWaypoint(0, new WorldPosition(owner.Longitude, owner.Latitude)),
        };
        var staged = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Missions = [.. snapshot.World.Missions,
                    new AlienMissionSnapshot(missionId, mission.Id, region, race, 1, 0, 1500, 1, -1)],
                Ufos = [.. snapshot.World.Ufos, ufo],
            },
        }, loaded.Content, new SplitMix64RandomSource(17));
        staged.BattleTerrainFiles = loaded.Files;
        if (Sources.TryGetValue(campaign, out var source)) Sources.AddOrUpdate(staged, source);
        var events = staged.Execute(new AdvanceCampaignTime(1)).Events;
        if (events.OfType<CampaignActionBlocked>().SingleOrDefault()?.Reason == WorldCommandReasons.BaseDefenseOpen)
            events = staged.Execute(new ResolveBaseDefense(owner.Id, Fire: false)).Events;
        Assert.DoesNotContain(events, static evt => evt is CampaignActionBlocked);
        return (staged, events.OfType<DeploymentRequested>().SingleOrDefault()?.Request, events);
    }

    /// <summary>
    /// Runs at the one-day speed for up to 40 days to the first deployment request. The headless player acknowledges
    /// every window, disengages interceptions, holds fire at base defenses and answers Yes to landings; with
    /// <paramref name="dispatch"/> it also sends a manned craft to the first landed or crashed UFO or mission site in
    /// view while none is out.
    /// </summary>
    private static (int Ticks, CampaignDeploymentRequest? Request, string? Stop) RunToDeployment(CampaignState campaign, bool dispatch)
    {
        const int Limit = 40 * 17280;
        var ticks = 0;
        while (ticks < Limit)
        {
            if (dispatch) Dispatch(campaign);
            var result = campaign.Execute(new AdvanceCampaignTime(Math.Min(Limit - ticks, 1440)) { SelectedSpeed = CampaignTimeSpeed.OneDay });
            ticks += result.Events.OfType<CampaignTimeAdvanced>().SingleOrDefault()?.Summary.TickCount ?? 0;
            if (result.Events.OfType<DeploymentRequested>().FirstOrDefault() is { } requested) return (ticks, requested.Request, null);
            if (result.Events.OfType<CampaignActionBlocked>().SingleOrDefault()?.Reason is not { } reason) continue;
            if (reason == CampaignState.MonthlyReportOpen) campaign.Execute(new AcknowledgeMonthlyReport());
            else if (reason == CampaignState.EventWindowOpen)
                while (campaign.GetQuery<ICampaignEventWindowQuery>()!.QueryEventWindows().Count != 0) campaign.Execute(new AcknowledgeCampaignEvent());
            else if (reason == WorldCommandReasons.LandingConfirmationsOpen)
            {
                foreach (var landing in campaign.GetQuery<ICampaignLandingQuery>()!.QueryLandingConfirmations())
                {
                    var answer = campaign.Execute(new AnswerLanding(landing.Confirmation, LandingAnswer.Yes)).Events;
                    if (answer.OfType<DeploymentRequested>().FirstOrDefault() is { } yes) return (ticks, yes.Request, null);
                }
            }
            else if (reason == WorldCommandReasons.BaseDefenseOpen)
            {
                var defense = campaign.GetQuery<ICampaignBaseDefenseQuery>()!.QueryBaseDefense()!;
                var events = campaign.Execute(new ResolveBaseDefense(defense.BaseId, Fire: false)).Events;
                if (events.OfType<DeploymentRequested>().FirstOrDefault() is { } attacked) return (ticks, attacked.Request, null);
            }
            else if (reason == WorldCommandReasons.InterceptionsOpen)
            {
                var interceptions = campaign.GetQuery<ICampaignInterceptionQuery>()!;
                foreach (var window in interceptions.QueryInterceptions())
                    if (window.AvailableModes.Contains(InterceptionMode.Disengage))
                        campaign.Execute(new SetInterceptionMode(window.Number, InterceptionMode.Disengage));
                for (var step = 0; step < 100_000 && interceptions.QueryInterceptions().Any(static window => !window.Minimized); step++)
                    campaign.Execute(new AdvanceInterceptions());
            }
            else return (ticks, null, reason);
        }
        return (ticks, null, "limit");
    }

    private static void Dispatch(CampaignState campaign)
    {
        var world = campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld();
        if (world.Crafts.Any(static craft => craft.Destination is { Kind: not WorldTargetKind.Base })) return;
        var target = world.Targets.FirstOrDefault(static target => target.Kind == WorldTargetKind.MissionSite ||
            target.Kind == WorldTargetKind.Ufo && target.Detected && target.Status is "Landed" or "Crashed");
        if (target is null) return;
        foreach (var craft in world.Crafts.Where(static craft => craft.Soldiers > 0 && craft.DispatchUnavailable is null))
            if (!campaign.Execute(new DispatchCraftToTarget(craft.BaseId, craft.TypeId, craft.Id, target.Reference)).Events
                .OfType<CampaignActionBlocked>().Any())
                return;
    }
}
