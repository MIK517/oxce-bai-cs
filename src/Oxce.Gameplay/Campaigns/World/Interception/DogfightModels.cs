namespace Oxce.Gameplay.Campaigns.World.Interception;

/// <summary>Dogfight-relevant craft or UFO statistics (<c>RuleCraftStats</c>, after bonuses).</summary>
internal readonly record struct DogfightStats(
    int DamageMaximum,
    int SpeedMaximum,
    int Acceleration,
    int HitBonus,
    int AvoidBonus,
    int PowerBonus,
    int Armor,
    int ShieldCapacity,
    int ShieldRecharge,
    int ShieldBleedThrough);

/// <summary>The dogfight properties of a craft weapon rule (<c>RuleCraftWeapon</c>).</summary>
internal sealed record DogfightWeaponRule(
    int Damage,
    int ShieldDamageModifier,
    int Range,
    int Accuracy,
    int ReloadCautious,
    int ReloadStandard,
    int ReloadAggressive,
    int AmmoMaximum,
    DogfightProjectileType ProjectileType,
    int ProjectileSpeed,
    int TractorBeamPower)
{
    /// <summary>The clip's or launcher's damage type when the weapon uses the unified damage formula
    /// (<c>CraftWeapon::fire</c>); null for the vanilla 50-100% roll.</summary>
    public IDogfightDamageType? UnifiedDamage { get; init; }
}

/// <summary><c>RuleDamageType::getRandomDamage</c> of a unified-formula craft weapon's item.</summary>
internal interface IDogfightDamageType
{
    int RandomDamage(int power, Oxce.Core.Random.IRandomSource random);
}

/// <summary>An installed craft weapon (<c>CraftWeapon</c>): rule, ammunition and the remembered disabled flag.</summary>
internal sealed class DogfightWeapon(DogfightWeaponRule rule, int ammo, bool disabled)
{
    public DogfightWeaponRule Rule { get; } = rule;
    public int Ammo { get; private set; } = ammo;
    public bool Disabled { get; set; } = disabled;

    /// <summary>CraftWeapon::setAmmo: clamps to [0, ammoMax]; false when the request was negative.</summary>
    public bool SetAmmo(int ammo)
    {
        Ammo = ammo;
        if (Ammo < 0)
        {
            Ammo = 0;
            return false;
        }
        if (Ammo > Rule.AmmoMaximum) Ammo = Rule.AmmoMaximum;
        return true;
    }
}

/// <summary>Pilot statistics with soldier bonuses only (<c>Soldier::getStatsWithSoldierBonusesOnly</c>).</summary>
internal readonly record struct DogfightPilot(int Firing, int Reactions, int Bravery);

/// <summary>The craft side of one interception. The world owns the craft; this view carries the
/// fields the dogfight reads and writes, and the world copies them back after each step.</summary>
internal sealed class DogfightCraft
{
    public required object Key { get; init; }
    public required DogfightStats Stats { get; init; }
    public int Damage { get; set; }
    public int Shield { get; set; }
    public bool LowFuel { get; set; }
    public bool InDogfight { get; set; }
    public int InterceptionOrder { get; set; }
    public int MissilePower { get; init; }
    public bool IsMissile => MissilePower > 0;
    public bool AllowLanding { get; init; } = true;
    public int WeaponSlots { get; init; }
    public required IReadOnlyList<DogfightWeapon?> Weapons { get; init; }
    public required IReadOnlyList<DogfightPilot> Pilots { get; init; }

    /// <summary>Craft::setDamage.</summary>
    public void SetDamage(int damage) => Damage = Math.Max(0, damage);

    /// <summary>Craft::setShield.</summary>
    public void SetShield(int shield) => Shield = Math.Max(0, Math.Min(Stats.ShieldCapacity, shield));

    /// <summary>Craft::getDamagePercentage.</summary>
    public int DamagePercentage => (int)Math.Floor((double)Damage / Stats.DamageMaximum * 100);

    /// <summary>Craft::isDestroyed.</summary>
    public bool IsDestroyed => Damage >= Stats.DamageMaximum;
}

/// <summary>Reference UFO statuses (<c>Ufo::UfoStatus</c>) as the dogfight sees them.</summary>
internal enum DogfightUfoStatus
{
    Flying,
    Landed,
    Crashed,
    Destroyed,
}

/// <summary>The dogfight properties of a UFO rule (<c>RuleUfo</c>).</summary>
internal sealed record DogfightUfoRule(
    int WeaponRange,
    int WeaponPower,
    int WeaponReload,
    int Radius,
    int Score,
    int BreakOffTime,
    int SoftlockThreshold,
    int SplashdownSurvivalChance,
    bool Unmanned,
    int BlobSize);

/// <summary>The UFO side of one or more interceptions. Up to four windows share one instance.</summary>
internal sealed class DogfightUfo
{
    public required object Key { get; init; }
    public required DogfightUfoRule Rule { get; init; }
    public required DogfightStats Stats { get; init; }
    public double Longitude { get; init; }
    public double Latitude { get; init; }
    public int Damage { get; set; }
    public int Shield { get; set; }
    public int Speed { get; set; }
    public int EscapeCountdown { get; set; }
    public int FireCountdown { get; set; }
    public int TractorBeamSlowdown { get; set; }
    public int ShieldRechargeHandle { get; set; }
    public int ShootingAt { get; set; }
    public int HitFrame { get; set; }
    public int SoftlockShotCounter { get; set; }
    public bool InterceptionProcessed { get; set; }
    public bool HunterKiller { get; set; }
    public int HuntBehavior { get; init; }
    public DogfightUfoStatus Status { get; set; }
    public int SecondsRemaining { get; set; }
    public string Altitude { get; set; } = "";
    public int CrashId { get; set; }
    public int LandId { get; set; }
    /// <summary>Ufo::_shotDownByCraftId: the craft whose hit crashed or destroyed the UFO.</summary>
    public object? ShotDownBy { get; set; }
    /// <summary>The craft this hunter-killer is attacking (Ufo::getDestination as a craft), if any.</summary>
    public object? TargetCraft { get; init; }

    /// <summary>Ufo::setShield.</summary>
    public void SetShield(int shield) => Shield = Math.Max(0, Math.Min(Stats.ShieldCapacity, shield));

    /// <summary>Ufo::isDestroyed.</summary>
    public bool IsDestroyed => Damage >= Stats.DamageMaximum;

    /// <summary>Ufo::isCrashed: kamikaze (hunt behavior 1) and unmanned UFOs never crash-land.</summary>
    public bool IsCrashed => IsDestroyed || HuntBehavior != 1 && !Rule.Unmanned && Damage > Stats.DamageMaximum / 2;
}

/// <summary><c>CraftWeaponProjectileType</c>; the order matches the reference blob order.</summary>
internal enum DogfightProjectileType
{
    StingrayMissile,
    AvalancheMissile,
    CannonRound,
    FusionBall,
    LaserBeam,
    PlasmaBeam,
}

/// <summary><c>Directions</c> of a projectile.</summary>
internal enum DogfightDirection
{
    None,
    Up,
    Down,
}

/// <summary>A projectile in flight. Reference: <c>Savegame/CraftWeaponProjectile.cpp</c>.</summary>
internal sealed class DogfightProjectile(IDogfightDamageType? damageItem)
{
    public const int HorizontalLeft = -1;
    public const int HorizontalCenter = 0;
    public const int HorizontalRight = 1;

    private int _speed;
    private int _distanceCovered;

    public IDogfightDamageType? DamageItem { get; } = damageItem;
    public DogfightProjectileType Type { get; private set; } = DogfightProjectileType.CannonRound;
    public bool IsBeam { get; private set; }
    public DogfightDirection Direction { get; private set; }
    public int Position { get; set; }
    public int HorizontalPosition { get; set; }
    public int State { get; private set; }
    public int Accuracy { get; set; }
    public int Damage { get; set; }
    public int Range { get; set; }
    public bool ToBeRemoved { get; private set; }
    public bool Missed { get; set; }
    public int ShieldDamageModifier { get; set; } = 100;

    /// <summary>CraftWeaponProjectile::setType: beams start at state 8.</summary>
    public void SetType(DogfightProjectileType type)
    {
        Type = type;
        if (type >= DogfightProjectileType.LaserBeam)
        {
            IsBeam = true;
            State = 8;
        }
    }

    /// <summary>CraftWeaponProjectile::setDirection: an interceptor shot starts at the craft.</summary>
    public void SetDirection(DogfightDirection direction)
    {
        Direction = direction;
        if (Direction == DogfightDirection.Up) Position = 0;
    }

    public void SetSpeed(int speed) => _speed = speed;

    public void Remove() => ToBeRemoved = true;

    /// <summary>CraftWeaponProjectile::move.</summary>
    public void Move()
    {
        if (!IsBeam)
        {
            var positionChange = _speed;
            if (_distanceCovered / 8 < Range && (_distanceCovered + _speed) / 8 >= Range)
                positionChange = Range * 8 - _distanceCovered;
            if (_distanceCovered / 8 >= Range) Missed = true;
            if (Direction == DogfightDirection.Up) Position += positionChange;
            else if (Direction == DogfightDirection.Down) Position -= positionChange;
            _distanceCovered += positionChange;
        }
        else
        {
            State /= 2;
            if (State == 1) ToBeRemoved = true;
        }
    }
}

/// <summary>The dogfight modes (the reference's radio buttons).</summary>
internal enum DogfightMode
{
    Standoff,
    Cautious,
    Standard,
    Aggressive,
    Disengage,
}
