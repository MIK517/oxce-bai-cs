using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Gameplay.Campaigns.World.Interception;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 5 S3: the dogfight engine replays every <c>strategic-dogfight</c> scenario (the reference
/// DogfightState compiled verbatim) step by step: the start state, every step row, the final
/// world-facing state and the exact sequence of random calls.
/// </summary>
public sealed class StrategicDogfightOracleTests
{
    public static TheoryData<string> Scenarios()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-dogfight");
        var data = new TheoryData<string>();
        foreach (var scenario in TestFixtures.Rows(oracle.RootElement, "scenarios"))
            data.Add(scenario.GetProperty("name").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void EngineMatchesTheReferenceStepByStep(string name)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-dogfight");
        var expected = TestFixtures.Rows(oracle.RootElement, "scenarios").Single(s => s.GetProperty("name").GetString() == name);
        var input = expected.GetProperty("input");
        var world = new OracleWorld(input);
        var dogfight = world.CreateDogfight();

        Assert.Equal(expected.GetProperty("start").GetRawText(), Json([
            dogfight.CurrentDistance, dogfight.TargetDistance, ModeName(dogfight.Mode), dogfight.PilotAccuracyBonus,
            dogfight.PilotDodgeBonus, dogfight.PilotApproachSpeedModifier, dogfight.CraftAccelerationBonus,
            world.Ufo.EscapeCountdown, world.Ufo.FireCountdown, dogfight.UfoSize]));

        var rows = expected.GetProperty("steps").EnumerateArray().ToArray();
        var commands = input.GetProperty("commands").EnumerateArray()
            .Select(static c => (Step: c[0].GetInt32(), Command: c[1].GetString()![0])).ToArray();
        var steps = input.GetProperty("steps").GetInt32();
        var step = 0;
        for (; step < steps && !dogfight.Ended; ++step)
        {
            foreach (var (at, command) in commands)
            {
                if (at != step) continue;
                switch (command)
                {
                    case 'S': dogfight.SelectMode(DogfightMode.Standoff); break;
                    case 'C': dogfight.SelectMode(DogfightMode.Cautious); break;
                    case 'N': dogfight.SelectMode(DogfightMode.Standard); break;
                    case 'A': dogfight.SelectMode(DogfightMode.Aggressive); break;
                    case 'D': dogfight.SelectMode(DogfightMode.Disengage); break;
                    case 'M': dogfight.Minimized = true; break;
                    case 'R': dogfight.Minimized = false; break;
                }
            }
            // GeoscapeState::handleDogfights clears the flag for every window before thinking.
            world.Ufo.InterceptionProcessed = false;
            dogfight.Think();
            Assert.True(step < rows.Length, $"{name}: the engine ran past the reference's last step {rows.Length - 1}.");
            Assert.Equal(rows[step].GetRawText(), Row(step, dogfight, world));
        }
        Assert.Equal(rows.Length, step);

        Assert.Equal(expected.GetProperty("end").GetRawText(), Json([
            world.Ufo.Damage, (int)world.Ufo.Status, world.Ufo.SecondsRemaining, world.Ufo.CrashId, world.Ufo.LandId,
            world.Ufo.Altitude.Length, world.Craft.Damage, world.Craft.InDogfight ? 1 : 0, world.Returned, world.Thought,
            world.UfoMoves, world.HunterResets, world.Ufo.HunterKiller ? 1 : 0, world.CountryActivity, world.RegionActivity,
            world.ShotDown, world.Missions, world.Experience, dogfight.DestroyUfo ? 1 : 0, dogfight.DestroyCraft ? 1 : 0,
            world.Ufo.SoftlockShotCounter, world.Ufo.ShieldRechargeHandle, world.Interrupted ? 1 : 0]));
        Assert.Equal(expected.GetProperty("rng").GetRawText(), Json([.. world.RandomCalls]));
    }

    private static string Row(int step, Dogfight d, OracleWorld world)
    {
        var weapons = new List<object>();
        for (var i = 0; i < world.Craft.Weapons.Count; ++i)
            weapons.Add(new object[] { world.Craft.Weapons[i]!.Ammo, d.WeaponFireCountdown(i), d.WeaponFireInterval(i) });
        var projectiles = d.Projectiles.Select(static p => new object[] { (int)p.Direction, (int)p.Type, p.Position, p.Missed ? 1 : 0 }).ToArray();
        var ufo = world.Ufo;
        return Json([step, d.CurrentDistance, d.TargetDistance, ModeName(d.Mode), ufo.Damage, ufo.Shield, (int)ufo.Status, ufo.Speed,
            ufo.EscapeCountdown, ufo.FireCountdown, ufo.TractorBeamSlowdown, ufo.ShootingAt, world.Craft.Damage, world.Craft.Shield,
            weapons, projectiles, d.End ? 1 : 0, d.Ended ? 1 : 0, d.UfoBreakingOff ? 1 : 0, d.Timeout, d.Status]);
    }

    private static string ModeName(DogfightMode mode) => mode.ToString().ToLowerInvariant();

    private static string Json(object[] values) => JsonSerializer.Serialize(values);

    private static int[] Ints(JsonElement element) => [.. element.EnumerateArray().Select(static v => v.GetInt32())];

    private static DogfightStats Stats(JsonElement s) => new(s[0].GetInt32(), s[1].GetInt32(), s[2].GetInt32(), s[3].GetInt32(),
        s[4].GetInt32(), s[5].GetInt32(), s[6].GetInt32(), s[7].GetInt32(), s[8].GetInt32(), s[9].GetInt32());

    /// <summary>The probe's stub world: scripted raw RNG and fixed globe, mission and region answers.</summary>
    private sealed class OracleWorld : IDogfightWorld, IRandomSource
    {
        private readonly ulong[] _script;
        private readonly int[] _world;
        private int _next;
        private bool _craftTargetsUfo = true;
        private int _nextId = 1;

        public OracleWorld(JsonElement input)
        {
            _script = [.. input.GetProperty("rng").EnumerateArray().Select(static v => v.GetUInt64())];
            _world = Ints(input.GetProperty("world"));
            Settings = CampaignInterceptionSettings.Default with
            {
                UfoGlancingHitThreshold = _world[8],
                UfoFiringRateCoefficients = Ints(input.GetProperty("firing")),
                UfoEscapeCountdownCoefficients = Ints(input.GetProperty("escape")),
                RetaliationTriggerOdds = Ints(input.GetProperty("trigger")),
                RetaliationBaseRegionOdds = Ints(input.GetProperty("baseRegion")),
            };
            var craftState = Ints(input.GetProperty("craftState"));
            var weapons = input.GetProperty("weapons").EnumerateArray().Select((w, index) =>
            {
                var v = Ints(w);
                var rule = new DogfightWeaponRule(v[0], v[11], v[1], v[2], v[3], v[4], v[5], v[7], (DogfightProjectileType)v[8], v[9], v[10])
                {
                    UnifiedDamage = index == 0 && _world[12] >= 0 ? new DogfightItemDamage(_world[12], _world[13], Settings) : null,
                };
                return (DogfightWeapon?)new DogfightWeapon(rule, v[6], index == 0 && _world[11] == 1);
            }).ToArray();
            Craft = new DogfightCraft
            {
                Key = new object(),
                Stats = Stats(input.GetProperty("craft")),
                Damage = craftState[0],
                Shield = craftState[1],
                MissilePower = craftState[2],
                WeaponSlots = weapons.Length,
                Weapons = weapons,
                Pilots = [.. input.GetProperty("pilots").EnumerateArray().Select(static p => new DogfightPilot(p[0].GetInt32(), p[1].GetInt32(), p[2].GetInt32()))],
                InDogfight = true,
            };
            var rule = Ints(input.GetProperty("ufoRule"));
            var state = Ints(input.GetProperty("ufoState"));
            UfoIsAttacking = state[5] == 1;
            Ufo = new DogfightUfo
            {
                Key = new object(),
                Rule = new DogfightUfoRule(rule[0], rule[1], rule[2], rule[3], rule[4], rule[5], rule[6], rule[7], rule[8] == 1, rule[9]),
                Stats = Stats(input.GetProperty("ufo")),
                Damage = state[0],
                Shield = state[1],
                Speed = state[2],
                HuntBehavior = state[3],
                HunterKiller = state[4] == 1,
                TargetCraft = UfoIsAttacking ? Craft.Key : null,
                Altitude = WorldAltitudes.High,
            };
            Missions = _world[7];
        }

        public DogfightCraft Craft { get; }
        public DogfightUfo Ufo { get; }
        public bool UfoIsAttacking { get; }
        public List<int[]> RandomCalls { get; } = [];
        public int Returned { get; private set; }
        public int Thought { get; private set; }
        public int UfoMoves { get; private set; }
        public int HunterResets { get; private set; }
        public int CountryActivity { get; private set; }
        public int RegionActivity { get; private set; }
        public int ShotDown { get; private set; }
        public int Missions { get; private set; }
        public int Experience { get; private set; }
        public bool Interrupted { get; private set; }

        public Dogfight CreateDogfight() => new(this, Craft, Ufo, UfoIsAttacking, 1);

        public IRandomSource Random => this;
        public CampaignInterceptionSettings Settings { get; }
        public int Difficulty => _world[0];
        public int MonthsPassed => _world[2];
        public bool DogfightAI => _world[9] == 1;

        // RandomState::generate over the scripted raw values; zero once the script is exhausted.
        public int NextInclusive(int minimum, int maximum)
        {
            var raw = _next < _script.Length ? _script[_next] : 0;
            _next++;
            var result = unchecked((int)(raw % (ulong)(long)(maximum - minimum + 1) + (ulong)(long)minimum));
            RandomCalls.Add([minimum, maximum, result]);
            return result;
        }

        public int NextExclusive(int exclusiveMaximum) => throw new InvalidOperationException("The dogfight draws inclusive ranges only.");
        public double NextUnit() => throw new InvalidOperationException("The dogfight draws inclusive ranges only.");

        public bool CraftTargetsUfo(DogfightCraft craft, DogfightUfo ufo) => _craftTargetsUfo;
        public void DogfightExperience() => Experience++;
        public bool InsideLand(DogfightUfo ufo) => _world[5] == 1;
        public bool InsideFakeUnderwater(DogfightUfo ufo) => _world[6] == 1;
        public void MoveUfo(DogfightUfo ufo) => UfoMoves++;
        public void CraftPursueUfo(DogfightCraft craft, DogfightUfo ufo) => _craftTargetsUfo = true;

        public void CraftReturnToBase(DogfightCraft craft)
        {
            Returned++;
            _craftTargetsUfo = false;
        }

        public bool CraftThink(DogfightCraft craft)
        {
            Thought++;
            return false;
        }

        public void StopHuntingCraft(DogfightCraft craft) { }
        public void ReturnUnlandableFollowers(DogfightUfo ufo) { }
        public void ResetHunterDestination(DogfightUfo ufo, DogfightCraft craft) => HunterResets++;
        public void ClearUfoDestination(DogfightUfo ufo) { }
        public void UfoShotDown(DogfightUfo ufo) => ShotDown++;
        public int MissionRetaliationOdds(DogfightUfo ufo) => _world[3];
        public int RetaliationAggression(DogfightUfo ufo) => _world[4];
        public string UfoMissionRegion(DogfightUfo ufo) => "REGION_UFO";
        public string? CraftBaseRegion(DogfightCraft craft) => "REGION_BASE";
        public bool RetaliationMissionExists(string region, DogfightUfo ufo) => _world[7] == 1;
        // The probe's new AlienMission takes the next ID from the same counter as the crash site.
        public void StartRetaliation(string region, DogfightUfo ufo)
        {
            Missions++;
            _nextId++;
        }

        public void AddXcomActivity(DogfightUfo ufo, int score)
        {
            CountryActivity += score;
            RegionActivity += score;
        }

        public int NextId(string key) => _nextId++;
        public int WaveInterruptPercentage(DogfightUfo ufo) => Math.Max(0, _world[10]);
        public void InterruptMission(DogfightUfo ufo) => Interrupted = true;
    }
}
