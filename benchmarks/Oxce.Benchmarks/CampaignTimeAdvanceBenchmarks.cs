using BenchmarkDotNet.Attributes;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using static Oxce.Benchmarks.PublicCampaignFixtures;

namespace Oxce.Benchmarks;

/// <summary>
/// Geoscape time advance on public strategic fixtures. Results are reported per five-second tick.
/// The reference performs 17,280 of these per in-game day, so this path bounds fast-forward speed.
/// Each scenario is validated outside the timed region: the full interval advances unblocked, the
/// actor population is unchanged, and pursuit or escort formations are still active at the end.
/// </summary>
[MemoryDiagnoser]
public class CampaignTimeAdvanceBenchmarks
{
    /// <summary>One in-game hour, so ten-minute, half-hour and hourly triggers are included.</summary>
    public const int Ticks = 720;

    public enum Scenario
    {
        /// <summary>Ordinary flying UFOs on long legs; no crafts airborne.</summary>
        Flight8,
        Flight32,
        Flight128,
        /// <summary>32 detected UFOs; two stationary crafts pursue one of them inside base radar.</summary>
        Pursuit32,
        /// <summary>8 missions, each with one leader and three hunter-killer escorts; one craft airborne.</summary>
        Escorts32,
    }

    private RuntimeContent _content = null!;
    private CampaignSnapshot _snapshot = null!;
    private CampaignState _campaign = null!;

    [Params(Scenario.Flight8, Scenario.Flight32, Scenario.Flight128, Scenario.Pursuit32, Scenario.Escorts32)]
    public Scenario Case { get; set; }

    /// <summary>
    /// Five-second speed enables native fast hunter retargeting on every tick; one-day speed
    /// retargets only on ten-minute ticks.
    /// </summary>
    [Params(CampaignTimeSpeed.FiveSeconds, CampaignTimeSpeed.OneDay)]
    public CampaignTimeSpeed Speed { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _content = LoadContent(["strategic-world.rul", "strategic-hunting.rul"]);
        _snapshot = Case switch
        {
            Scenario.Flight8 => Flight(8, detected: false),
            Scenario.Flight32 => Flight(32, detected: false),
            Scenario.Flight128 => Flight(128, detected: false),
            Scenario.Pursuit32 => Pursuit(32),
            Scenario.Escorts32 => Escorts(8, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(Case)),
        };
        Validate();
    }

    [IterationSetup]
    public void Restore() => _campaign = CampaignState.Restore(_snapshot, _content, new SplitMix64RandomSource(40));

    /// <summary>Advances the full interval, resuming after notification pauses as the geoscape does.</summary>
    [Benchmark(OperationsPerInvoke = Ticks)]
    public int Advance()
    {
        var remaining = Ticks;
        while (remaining > 0)
        {
            var result = _campaign.Execute(new AdvanceCampaignTime(remaining) { SelectedSpeed = Speed });
            var advanced = ((CampaignTimeAdvanced)result.Events[0]).Summary.TickCount;
            if (advanced == 0 || result.Events[^1] is CampaignActionBlocked) break;
            remaining -= advanced;
        }
        return Ticks - remaining;
    }

    private CampaignSnapshot Flight(int count, bool detected)
    {
        var initial = PlacedCampaign(_content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [Mission(4, "MISSION_SCOUT", count)],
                Ufos = [.. Enumerable.Range(0, count).Select(index => FlyingUfo(9 + index, 3 + index, "UFO_SCOUT", 4,
                    0.4 + index * 0.01, 0.2 - index * 0.002, 50) with { Detected = detected })],
            },
        };
        return WithNextId(WithNextId(initial, "STR_UFO", 3 + count), "STR_LANDING_SITE", 1);
    }

    private CampaignSnapshot Pursuit(int count)
    {
        var initial = Flight(count, detected: true);
        // Base radar keeps the target detected. The fixture's zero-speed crafts pursue it for the
        // whole interval without ever intercepting, so every tick runs the pursuit preflight.
        var target = initial.World.Ufos[^1] with { Longitude = 0.35, Latitude = 0.1 };
        initial = initial with
        {
            World = initial.World with { Ufos = [.. initial.World.Ufos.Take(count - 1), target] },
            Bases = [initial.Bases[0] with
            {
                Facilities = [.. initial.Bases[0].Facilities, new FacilitySnapshot("RADAR_TEST", 1, 0, 0, 0, false, false, false)],
            }, .. initial.Bases.Skip(1)],
        };
        var campaign = CampaignState.Restore(initial, _content, new SplitMix64RandomSource(40));
        foreach (var owner in initial.Bases)
            foreach (var craft in owner.Crafts.Where(craft => craft.RuleId != "SHIP"))
            {
                var result = campaign.Execute(new DispatchCraftToTarget(owner.Id, craft.RuleId, craft.Id,
                    new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, target.Id, 0, 0)
                    { UniqueId = target.UniqueId }));
                if (result.Events.OfType<CampaignActionBlocked>().FirstOrDefault() is { } blocked)
                    throw new InvalidOperationException($"Pursuit dispatch was refused: {blocked.Reason}");
            }
        return campaign.Capture();
    }

    private CampaignSnapshot Escorts(int missions, int escortsPerLeader)
    {
        var initial = PlacedCampaign(_content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        var ufos = new List<UfoSnapshot>();
        for (var mission = 0; mission < missions; mission++)
        {
            var longitude = 0.6 + mission * 0.2;
            ufos.Add(FlyingUfo(100 + ufos.Count, 3 + ufos.Count, "UFO_SCOUT", 10 + mission, longitude, 0.3, 50));
            for (var escort = 0; escort < escortsPerLeader; escort++)
                ufos.Add(FlyingUfo(100 + ufos.Count, 3 + ufos.Count, "ESCORT_HUNTER", 10 + mission,
                    longitude - 0.05 - escort * 0.01, 0.25, 2200) with
                { HunterKiller = true, Escort = true });
        }
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [.. Enumerable.Range(0, missions).Select(mission => Mission(10 + mission, "MISSION_SCOUT", 1 + escortsPerLeader))],
                Ufos = ufos,
            },
        };
        initial = WithNextId(WithNextId(initial, "STR_UFO", 3 + ufos.Count), "STR_LANDING_SITE", 1);
        // An airborne craft gives every hunter-killer retarget a candidate to evaluate.
        var campaign = CampaignState.Restore(initial, _content, new SplitMix64RandomSource(40));
        var ship = initial.Bases[0].Crafts.First(craft => craft.RuleId == "SHIP");
        var dispatched = campaign.Execute(new DispatchCraftToWaypoint(initial.Bases[0].Id, ship.RuleId, ship.Id, 5.5, -0.4));
        if (dispatched.Events.OfType<CampaignActionBlocked>().FirstOrDefault() is { } blocked)
            throw new InvalidOperationException($"Craft dispatch was refused: {blocked.Reason}");
        return campaign.Capture();
    }

    private void Validate()
    {
        Restore();
        var advanced = Advance();
        if (advanced != Ticks)
        {
            var result = _campaign.Execute(new AdvanceCampaignTime(1) { SelectedSpeed = Speed });
            throw new InvalidOperationException($"{Case} advanced {advanced} of {Ticks} ticks: " +
                (result.Events.OfType<CampaignActionBlocked>().FirstOrDefault()?.Reason ?? "no block reason"));
        }
        var after = _campaign.Capture();
        if (after.World.Ufos.Count != _snapshot.World.Ufos.Count || after.World.Missions.Count != _snapshot.World.Missions.Count)
            throw new InvalidOperationException($"{Case} changed its actor population.");
        if (after.World.Ufos.Any(ufo => ufo.Status != UfoStatus.Flying))
            throw new InvalidOperationException($"{Case} contains a UFO that stopped flying.");
        if (Case == Scenario.Pursuit32)
        {
            var crafts = after.Bases.SelectMany(owner => owner.Crafts).Where(craft => craft.RuleId != "SHIP").ToArray();
            if (crafts.Length == 0 || crafts.Any(craft => craft.Logistics is not { Status: "STR_OUT", Destination.Kind: WorldTargetKind.Ufo }))
                throw new InvalidOperationException("Pursuit ended before the measured interval did: " + string.Join("; ",
                    crafts.Select(craft => $"{craft.RuleId} {craft.Logistics?.Status} {craft.Logistics?.Destination?.Kind} fuel={craft.Logistics?.Fuel} low={craft.Logistics?.LowFuel}")) +
                    $" target detected={after.World.Ufos[^1].Detected}");
        }
        if (Case == Scenario.Escorts32 && after.World.Ufos.Any(ufo => ufo.HunterKiller && !ufo.Escorting))
            throw new InvalidOperationException("An escort formation broke during the measured interval.");
    }
}
