using BenchmarkDotNet.Attributes;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using static Oxce.Benchmarks.PublicCampaignFixtures;

namespace Oxce.Benchmarks;

/// <summary>
/// Branch 5 S10: dogfight timer steps (<see cref="AdvanceInterceptions"/>) of one open interception in a
/// populated world. The ship reaches its UFO in the setup tick; the window then closes in at standoff
/// for the measured steps without firing, so every step runs the full forecast and write-back. Results
/// are per step; the reference runs one every 30 ms while a window is open.
/// </summary>
[MemoryDiagnoser]
public class InterceptionStepBenchmarks
{
    public const int Steps = 200;

    private const string Rules = """
        crafts:
          - type: SHIP
            weapons: 2
        craftWeapons:
          - type: BENCH_CANNON
            launcher: SUPPLY
            clip: BULKY
            ammoMax: 200
            rearmRate: 200
            damage: 10
            range: 10
            accuracy: 25
            reloadCautious: 24
            reloadStandard: 16
            reloadAggressive: 8
            projectileType: 2
            projectileSpeed: 8

        """;

    private RuntimeContent _content = null!;
    private CampaignSnapshot _snapshot = null!;
    private CampaignState _campaign = null!;

    /// <summary>Flying UFOs in the world besides the intercepted one.</summary>
    [Params(8, 128)]
    public int Ufos { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _content = LoadContent(["strategic-world.rul"], Rules);
        var initial = PlacedCampaign(_content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        var target = FlyingUfo(9, 3, "UFO_SCOUT", 4, 0.4, 0.2, 1000) with { Detected = true };
        var owner = initial.Bases[0];
        var crafts = owner.Crafts.Select(craft => craft.RuleId != "SHIP" || craft.Logistics is null ? craft : craft with
        {
            Logistics = craft.Logistics with
            {
                Status = "STR_OUT",
                Fuel = 100,
                Speed = 6000,
                Longitude = target.Longitude,
                Latitude = target.Latitude,
                Weapons = [new CraftWeaponSnapshot("BENCH_CANNON", 200, false, false), null],
                Items = new Dictionary<string, int>(),
                Destination = new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, target.Id,
                    target.Longitude, target.Latitude)
                { UniqueId = target.UniqueId },
            },
        }).ToArray();
        initial = initial with
        {
            Bases = [owner with { Crafts = crafts }, .. initial.Bases.Skip(1)],
            World = initial.World with
            {
                Missions = [Mission(4, "MISSION_SCOUT", Ufos + 1)],
                Ufos = [target, .. Enumerable.Range(0, Ufos).Select(index => FlyingUfo(10 + index, 4 + index, "UFO_SCOUT", 4,
                    1.0 + index * 0.01, -0.2 - index * 0.002, 50))],
            },
        };
        _snapshot = WithNextId(WithNextId(initial, "STR_UFO", 4 + Ufos), "STR_LANDING_SITE", 1);
        Restore();
        if (Advance() != Steps) throw new InvalidOperationException("The interception ended before the measured steps.");
    }

    /// <summary>Interceptions are not saved, so each iteration opens the window again with one tick.</summary>
    [IterationSetup]
    public void Restore()
    {
        _campaign = CampaignState.Restore(_snapshot, _content, new SplitMix64RandomSource(40));
        var result = _campaign.Execute(new AdvanceCampaignTime(1));
        if (!result.Events.OfType<InterceptionStarted>().Any())
            throw new InvalidOperationException("The setup tick did not open the interception.");
    }

    [Benchmark(OperationsPerInvoke = Steps)]
    public int Advance()
    {
        var steps = 0;
        for (; steps < Steps; steps++)
        {
            var result = _campaign.Execute(new AdvanceInterceptions());
            if (result.Events.Any(static evt => evt is CampaignActionBlocked or InterceptionEnded)) break;
        }
        return steps;
    }
}
