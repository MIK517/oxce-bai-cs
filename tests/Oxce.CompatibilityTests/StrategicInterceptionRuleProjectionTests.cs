using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 5 S2: the craft-weapon and UFO properties that interception reads are projected into the
/// runtime catalog with the reference defaults (<c>RuleCraftWeapon</c> and <c>RuleUfo</c>
/// constructors and <c>load</c> at the pinned reference).
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
}
