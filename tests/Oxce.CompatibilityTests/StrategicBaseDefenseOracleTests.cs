using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns.World.Interception;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 5 S7: the base defense rounds replay every <c>strategic-base-defense</c> scenario (the
/// reference BaseDefenseState::nextStep and applyDamage compiled verbatim): the UFO's damage, shield,
/// status and wave interrupt, the passes and attacks, the ammunition left and every random call.
/// </summary>
public sealed class StrategicBaseDefenseOracleTests
{
    public static TheoryData<string> Scenarios()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-base-defense");
        var data = new TheoryData<string>();
        foreach (var scenario in TestFixtures.Rows(oracle.RootElement, "scenarios"))
            data.Add(scenario.GetProperty("name").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void DefenseRoundsMatchTheReference(string name)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-base-defense");
        var expected = TestFixtures.Rows(oracle.RootElement, "scenarios").Single(s => s.GetProperty("name").GetString() == name);
        var input = expected.GetProperty("input");
        var rows = input.GetProperty("defenses").EnumerateArray().Select(static row => row.EnumerateArray().Select(static v => v.GetInt32()).ToArray()).ToArray();
        var settings = CampaignInterceptionSettings.Default;
        // The probe shares one ammunition item whose damage type is the last defense's.
        var randomType = rows.Length == 0 ? 0 : rows[^1][7];
        var defenses = rows.Select(row => new BaseDefenseWeapon
        {
            HitRatio = row[0],
            DefenseValue = row[1],
            AmmoNeeded = row[2],
            HasOwnAmmo = row[3] > 0,
            Ammo = row[4],
            ShieldDamageModifier = row[5],
            AmmoItem = "AMMO",
            UnifiedDamage = row[6] != 0 ? new DogfightItemDamage(randomType, 0, settings) : null,
        }).ToArray();
        var u = input.GetProperty("ufo").EnumerateArray().Select(static v => v.GetInt32()).ToArray();
        var ufo = new BaseDefenseUfo
        {
            Damage = u[0],
            DamageMaximum = u[1],
            Shield = u[2],
            ShieldCapacity = u[3],
            AvoidBonus2 = u[4],
            Armor = u[5],
            ShieldBleedThrough = u[6],
            NeverCrashes = u[7] != 0 || u[8] == 1,
        };
        var storage = new Dictionary<string, int>(StringComparer.Ordinal);
        if (input.GetProperty("storageAmmo").GetInt32() is > 0 and var ammo) storage["AMMO"] = ammo;
        var random = new ScriptedRandom(input.GetProperty("rng").EnumerateArray().Select(static v => v.GetUInt64()).ToArray());
        var interrupted = false;
        var (passes, attacks) = BaseDefense.Fire(defenses, input.GetProperty("gravShields").GetInt32(), ufo, storage, random,
            () => u[9], () => interrupted = true);

        Assert.Equal(
            (expected.GetProperty("damage").GetInt32(), expected.GetProperty("shield").GetInt32(), expected.GetProperty("status").GetInt32(),
                expected.GetProperty("interrupted").GetBoolean(), expected.GetProperty("passes").GetInt32(), expected.GetProperty("attacks").GetInt32()),
            (ufo.Damage, ufo.Shield, (int)ufo.Status, interrupted, passes, attacks));
        Assert.Equal(expected.GetProperty("ammo").EnumerateArray().Select(static v => v.GetInt32()), defenses.Select(static d => d.Ammo));
        Assert.Equal(expected.GetProperty("storage").GetInt32(), storage.GetValueOrDefault("AMMO"));
        Assert.Equal(expected.GetProperty("rng").GetRawText(), JsonSerializer.Serialize(random.Calls));
    }

    // RandomState::generate over the scripted raw values; zero once the script is exhausted.
    private sealed class ScriptedRandom(ulong[] script) : IRandomSource
    {
        private int _next;

        public List<int[]> Calls { get; } = [];

        public int NextInclusive(int minimum, int maximum)
        {
            var raw = _next < script.Length ? script[_next] : 0;
            _next++;
            var result = unchecked((int)(raw % (ulong)(long)(maximum - minimum + 1) + (ulong)(long)minimum));
            Calls.Add([minimum, maximum, result]);
            return result;
        }

        public int NextExclusive(int exclusiveMaximum) => throw new InvalidOperationException("Base defense draws inclusive ranges only.");
        public double NextUnit() => throw new InvalidOperationException("Base defense draws inclusive ranges only.");
    }
}

/// <summary>Branch 5 S7: Base::setupDefenses and getGravShields over a base's facilities.</summary>
public sealed class StrategicBaseDefenseSetupTests
{
    private const string Facilities = """
        facilities:
          - type: DEF_OWN
            mapName: LOGISTICS
            defense: 500
            hitRatio: 60
            ammoMax: 4
            ammoNeeded: 2
          - type: DEF_STORES
            mapName: LOGISTICS
            defense: 300
            hitRatio: 50
            ammoItem: SUPPLY
            unifiedDamageFormula: true
            shieldDamageModifier: 150
          - type: DEF_GRAV
            mapName: LOGISTICS
            grav: true

        """;

    [Fact]
    public void BuiltDefensesFireInFacilityOrderAndGravShieldsAddPasses()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], Facilities);
        var snapshot = StrategicWorldTestSupport.CreateSnapshot(content, "Base defense setup");
        var owner = snapshot.Bases[0];
        var added = new[] { ("DEF_STORES", 0), ("DEF_OWN", 0), ("DEF_OWN", 3), ("DEF_GRAV", 0), ("DEF_GRAV", 0) }
            .Select((entry, index) => new Oxce.Gameplay.Campaigns.FacilitySnapshot(entry.Item1, index, 5, entry.Item2, 0, false, false, false));
        snapshot = snapshot with { Bases = [owner with { Facilities = [.. owner.Facilities, .. added] }, .. snapshot.Bases.Skip(1)] };
        var campaign = Oxce.Gameplay.Campaigns.CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(1));
        var world = (Oxce.Gameplay.Campaigns.World.CampaignWorld)campaign.GetQuery<Oxce.Gameplay.Campaigns.World.ICampaignInterceptionQuery>()!;
        var (defenses, gravShields) = world.BaseDefenses(snapshot.Bases[0].Id);
        Assert.Equal(2, gravShields);
        Assert.Collection(defenses,
            stores => Assert.Equal((50, 300, 1, false, "SUPPLY", 150, true), (stores.HitRatio, stores.DefenseValue, stores.AmmoNeeded,
                stores.HasOwnAmmo, stores.AmmoItem, stores.ShieldDamageModifier, stores.UnifiedDamage is not null)),
            own => Assert.Equal((60, 500, 2, true, (string?)null, 100, false), (own.HitRatio, own.DefenseValue, own.AmmoNeeded,
                own.HasOwnAmmo, own.AmmoItem, own.ShieldDamageModifier, own.UnifiedDamage is not null)));
    }
}
