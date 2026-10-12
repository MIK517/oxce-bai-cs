using Oxce.Mods.Rulesets.CampaignStart;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 5 S2: the craft, craft-weapon, UFO and global properties that interception reads are
/// projected into the runtime catalog with the reference defaults and layering (<c>RuleCraft</c>,
/// <c>RuleCraftWeapon</c>, <c>RuleUfo</c> and <c>Mod</c> constructors and loaders at the pinned reference).
/// </summary>
public sealed class StrategicInterceptionRuleProjectionTests
{
    [Fact]
    public void DogfightPropertiesProjectWithReferenceDefaults()
    {
        var rules = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            craftWeapons:
              - type: DOGFIGHT_CANNON
                launcher: SUPPLY
                clip: BULKY
                ammoMax: 50
                rearmRate: 50
                damage: 14
                shieldDamageModifier: 150
                range: 42
                accuracy: 25
                reloadCautious: 24
                reloadStandard: 16
                reloadAggressive: 8
                projectileType: 0
                projectileSpeed: 8
                weaponType: 1
                tractorBeamPower: 3
                unifiedDamageFormula: true
                underwaterOnly: true
            crafts:
              - type: DOGFIGHT_MISSILE
                missilePower: 5
                maxAltitude: 2
            ufos:
              - type: UFO_DOGFIGHT
                size: STR_MEDIUM
                power: 20
                range: 30
                reload: 40
                score: 150
                breakOffTime: 300
                softlockThreshold: 60
                missilePower: -1
                missileStopChance: 25
                splashdownSurvivalChance: 40
            """).RuntimeRules;

        var missile = rules.Crafts[rules.Crafts.GetRequired("DOGFIGHT_MISSILE")].Value;
        Assert.Equal((5, true, 2, true), (missile.MissilePower, missile.IsMissile, missile.MaximumAltitude, missile.IsWaterOnly));
        // RuleCraft defaults: missilePower 0, maxAltitude -1.
        var ship = rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value;
        Assert.Equal((0, false, -1, false), (ship.MissilePower, ship.IsMissile, ship.MaximumAltitude, ship.IsWaterOnly));

        var cannon = rules.CraftWeapons[rules.CraftWeapons.GetRequired("DOGFIGHT_CANNON")].Value;
        Assert.Equal((14, 150, 42, 25), (cannon.Damage, cannon.ShieldDamageModifier, cannon.Range, cannon.Accuracy));
        Assert.Equal((24, 16, 8), (cannon.ReloadCautious, cannon.ReloadStandard, cannon.ReloadAggressive));
        Assert.Equal((0, 8, 1, 3), (cannon.ProjectileType, cannon.ProjectileSpeed, cannon.WeaponType, cannon.TractorBeamPower));
        Assert.True(cannon.UnifiedDamageFormula);
        Assert.True(cannon.UnderwaterOnly);

        // RuleCraftWeapon defaults: shieldDamageModifier 100, CWPT_CANNON_ROUND, everything else zero.
        var radar = rules.CraftWeapons[rules.CraftWeapons.GetRequired("RADAR_BOOST")].Value;
        Assert.Equal((0, 100, 0, 0), (radar.Damage, radar.ShieldDamageModifier, radar.Range, radar.Accuracy));
        Assert.Equal((0, 0, 0), (radar.ReloadCautious, radar.ReloadStandard, radar.ReloadAggressive));
        Assert.Equal((2, 0, 0, 0), (radar.ProjectileType, radar.ProjectileSpeed, radar.WeaponType, radar.TractorBeamPower));
        Assert.False(radar.UnifiedDamageFormula);
        Assert.False(radar.UnderwaterOnly);

        var ufo = rules.Ufos[rules.Ufos.GetRequired("UFO_DOGFIGHT")].Value;
        Assert.Equal((20, 30, 40), (ufo.WeaponPower, ufo.WeaponRange, ufo.WeaponReload));
        Assert.Equal((150, 300, 60), (ufo.Score, ufo.BreakOffTime, ufo.SoftlockThreshold));
        Assert.Equal((-1, 25, 40), (ufo.MissilePower, ufo.MissileStopChance, ufo.SplashdownSurvivalChance));

        // RuleUfo defaults: softlockThreshold 100, splashdownSurvivalChance 100, the rest zero.
        var scout = rules.Ufos[rules.Ufos.GetRequired("UFO_SCOUT")].Value;
        Assert.Equal((0, 0, 100), (scout.Score, scout.BreakOffTime, scout.SoftlockThreshold));
        Assert.Equal((0, 0, 100), (scout.MissilePower, scout.MissileStopChance, scout.SplashdownSurvivalChance));
    }

    [Fact]
    public void InterceptionGlobalsUseReferenceDefaults()
    {
        var settings = StrategicReadinessTestContent.Load("strategic-world.rul").RuntimeRules.Campaign.Interception;

        Assert.Equivalent(CampaignInterceptionSettings.Default, settings, strict: true);
        Assert.Equal((0, false, 0, 1000), (settings.ChanceToStopRetaliation, settings.LessAliensDuringBaseDefense,
            settings.UfoGlancingHitThreshold, settings.UfoBeamWidthParameter));
        Assert.Equal([400, 200, 100, 50, 25], settings.UfoTractorBeamSizeModifiers);
        Assert.True(settings.EscortsJoinFightAgainstHK);
        Assert.Equal((100, 100), (settings.CrewEmergencyEvacuationSurvivalChance, settings.PilotsEmergencyEvacuationSurvivalChance));
        Assert.Equal((55, 40, 55, 60), (settings.PilotAccuracyZeroPoint, settings.PilotAccuracyRange,
            settings.PilotReactionsZeroPoint, settings.PilotReactionsRange));
        Assert.Equal([90, 80, 30], settings.PilotBraveryThresholds);
        Assert.False(settings.ShowDogfightDistanceInKm);
        Assert.Empty(settings.UfoFiringRateCoefficients);
        Assert.Empty(settings.RetaliationTriggerOdds);
        Assert.Equal([0, 1, 2, 3, 4], settings.DifficultyCoefficients);
        Assert.Equal(4, settings.DifficultyCoefficient(7));
        Assert.Equal([0, 0, 0, 0, 0], settings.DifficultyBasedRetaliationDelay);
    }

    [Fact]
    public void InterceptionGlobalsLayerLikeModLoadFile()
    {
        // The extra rules load first and the final rules last (descending file names).
        var settings = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            chanceToStopRetaliation: 30
            lessAliensDuringBaseDefense: true
            ufoGlancingHitThreshold: 5
            ufoBeamWidthParameter: 700
            ufoTractorBeamSizeModifiers: [1, 2, 3, 4, 5]
            escortsJoinFightAgainstHK: false
            crewEmergencyEvacuationSurvivalChance: 70
            pilotsEmergencyEvacuationSurvivalChance: 60
            pilotAccuracyZeroPoint: 50
            pilotAccuracyRange: 30
            pilotReactionsZeroPoint: 45
            pilotReactionsRange: 20
            pilotBraveryThresholds: [95, 85, 35]
            showDogfightDistanceInKm: true
            difficultyCoefficient: [1, 2, 3, 4, 5]
            difficultyBasedRetaliationDelay: [6, 5, 4, 3, 2]
            difficultyCoefficientOverrides:
              ufoFiringRateCoefficients: [150, 125, 100, 75, 50]
              ufoEscapeCountdownCoefficients: [80, 90]
              retaliationTriggerOdds: [5, 10, 15, 20, 25]
              retaliationBaseRegionOdds: [10]
              aliensFacingCraftOdds: [1, 2]
              monthlyRatingThresholds: [100, 200]
            """, finalRules: """
            ufoTractorBeamSizeModifiers: [9, 8]
            pilotBraveryThresholds: [70]
            difficultyBasedRetaliationDelay: [0, 0]
            difficultyCoefficientOverrides:
              ufoEscapeCountdownCoefficients: [120]
              retaliationTriggerOdds: []
            """).RuntimeRules.Campaign.Interception;

        Assert.Equal((30, true, 5, 700), (settings.ChanceToStopRetaliation, settings.LessAliensDuringBaseDefense,
            settings.UfoGlancingHitThreshold, settings.UfoBeamWidthParameter));
        Assert.False(settings.EscortsJoinFightAgainstHK);
        Assert.Equal((70, 60), (settings.CrewEmergencyEvacuationSurvivalChance, settings.PilotsEmergencyEvacuationSurvivalChance));
        Assert.Equal((50, 30, 45, 20), (settings.PilotAccuracyZeroPoint, settings.PilotAccuracyRange,
            settings.PilotReactionsZeroPoint, settings.PilotReactionsRange));
        Assert.True(settings.ShowDogfightDistanceInKm);
        // Fixed arrays: a later layer overrides only the slots it lists.
        Assert.Equal([9, 8, 3, 4, 5], settings.UfoTractorBeamSizeModifiers);
        Assert.Equal([70, 85, 35], settings.PilotBraveryThresholds);
        // Override lists: a later layer replaces the whole list, even with an empty one.
        Assert.Equal([150, 125, 100, 75, 50], settings.UfoFiringRateCoefficients);
        Assert.Equal([120], settings.UfoEscapeCountdownCoefficients);
        Assert.Empty(settings.RetaliationTriggerOdds);
        Assert.Equal([10], settings.RetaliationBaseRegionOdds);
        Assert.Equal([1, 2], settings.AliensFacingCraftOdds);
        Assert.Equal([100, 200], settings.MonthlyRatingThresholds);
        Assert.Equal([1, 2, 3, 4, 5], settings.DifficultyCoefficients);
        Assert.Equal([0, 0, 4, 3, 2], settings.DifficultyBasedRetaliationDelay);
    }
}
