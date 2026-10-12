using Oxce.Core.Random;

namespace Oxce.Gameplay.Campaigns.World.Interception;

/// <summary>
/// One interception window: the gameplay state of <c>DogfightState</c> and its step. Drawing,
/// sounds and window placement stay in the UI; everything that changes the fight's outcome is here,
/// including the status timeout that the reference decrements while animating. Reference:
/// <c>Geoscape/DogfightState.cpp</c> (constructor, think, update, animate, fireWeapon, ufoFireWeapon,
/// distance helpers, mode handlers, endDogfight) at 4df3a5e; pinned by the <c>strategic-dogfight</c> oracle.
/// </summary>
internal sealed class Dogfight
{
    public const int WeaponMaximum = 4;
    public const int StandoffDistance = 560;
    public const int AggressiveDistance = 64;
    private const int TimeoutAfterStatus = 50;

    private readonly IDogfightWorld _world;
    private readonly int[] _weaponFireInterval = new int[WeaponMaximum];
    private readonly int[] _weaponFireCountdown = new int[WeaponMaximum];
    private readonly bool[] _weaponEnabled = new bool[WeaponMaximum];
    private readonly bool[] _tractorLockedOn = new bool[WeaponMaximum];
    private readonly List<DogfightProjectile> _projectiles = [];
    private readonly int _weaponNum;
    private readonly int _pilotAccuracyBonus;
    private readonly int _pilotDodgeBonus;
    private readonly int _pilotApproachSpeedModifier;
    private readonly int _craftAccelerationBonus;
    private readonly bool _missileCraft;
    private readonly bool _disableDisengage;
    private readonly bool _disableStandoff;
    private readonly bool _disableCautious;
    private readonly bool _disableStandard;
    private readonly bool _disableAggressive;
    private readonly int _ufoSize;
    private bool _missileImpact;
    private bool _endUfoHandled;
    private bool _endCraftHandled;
    private bool _destroyCraft;
    private bool _animatingHit;

    /// <summary>DogfightState constructor: the gameplay statements, in reference order.</summary>
    public Dogfight(IDogfightWorld world, DogfightCraft craft, DogfightUfo ufo, bool ufoIsAttacking, int interceptionNumber)
    {
        _world = world;
        Craft = craft;
        Ufo = ufo;
        UfoIsAttacking = ufoIsAttacking;
        InterceptionNumber = interceptionNumber;
        _missileCraft = craft.IsMissile;
        Timeout = TimeoutAfterStatus;
        CurrentDistance = 640;
        TargetDistance = StandoffDistance;

        Craft.InDogfight = true;
        _weaponNum = Math.Min(craft.WeaponSlots, WeaponMaximum);
        for (var i = 0; i < _weaponNum; ++i)
        {
            _weaponEnabled[i] = true;
            _weaponFireCountdown[i] = 0;
            _tractorLockedOn[i] = false;
            if (Weapon(i) is { } w) _weaponEnabled[i] = !w.Disabled;
        }

        _pilotAccuracyBonus = AccuracyBonus(craft.Pilots, world.Settings);
        _pilotDodgeBonus = DodgeBonus(craft.Pilots, world.Settings);
        _pilotApproachSpeedModifier = ApproachSpeedModifier(craft.Pilots, world.Settings);
        _craftAccelerationBonus = 2;
        if (craft.Pilots.Count != 0) _craftAccelerationBonus = Math.Min(4, craft.Stats.Acceleration / 3 + 1);

        if (UfoIsAttacking)
        {
            _disableStandoff = true;
            _disableStandard = true;
            _disableAggressive = false;
            if (Ufo.Stats.SpeedMaximum >= Craft.Stats.SpeedMaximum) _disableDisengage = true;
            if (_weaponNum == 0) _disableCautious = true;
            if (Ufo.TargetCraft is { } target)
            {
                if (!ReferenceEquals(Craft.Key, target)) CurrentDistance += 16;
                else _pilotApproachSpeedModifier = 4;
            }
        }
        if (_missileCraft)
        {
            _disableStandoff = true;
            _disableStandard = true;
            _disableAggressive = false;
            _disableDisengage = true;
            _disableCautious = true;
            _pilotApproachSpeedModifier = 4;
        }

        Mode = UfoIsAttacking || _missileCraft ? DogfightMode.Aggressive : DogfightMode.Standoff;
        if (UfoIsAttacking || _missileCraft) PressAggressive();

        if (Ufo.EscapeCountdown == 0)
        {
            Ufo.FireCountdown = 0;
            var breakOff = Ufo.Rule.BreakOffTime;
            var escapeCountdown = breakOff + Random.NextInclusive(0, breakOff) - 30 * DifficultyCoefficient;
            var custom = world.Settings.UfoEscapeCountdownCoefficients;
            if (custom.Count > world.Difficulty)
            {
                escapeCountdown = breakOff + Random.NextInclusive(0, breakOff);
                escapeCountdown = escapeCountdown * custom[world.Difficulty] / 100;
            }
            Ufo.EscapeCountdown = Math.Max(1, escapeCountdown);
        }

        for (var i = 0; i < _weaponNum; ++i)
            if (Weapon(i) is { } w)
                _weaponFireInterval[i] = UfoIsAttacking ? w.Rule.ReloadAggressive : w.Rule.ReloadStandard;

        UfoBlobSize = Ufo.Rule.BlobSize;
        _ufoSize = Math.Min(UfoBlobSize, 4);

        if (Ufo.ShieldRechargeHandle == 0) Ufo.ShieldRechargeHandle = InterceptionNumber;
    }

    private Dogfight(Dogfight source, IDogfightWorld world, DogfightCraft craft, DogfightUfo ufo)
    {
        _world = world;
        Craft = craft;
        Ufo = ufo;
        _weaponFireInterval = (int[])source._weaponFireInterval.Clone();
        _weaponFireCountdown = (int[])source._weaponFireCountdown.Clone();
        _weaponEnabled = (bool[])source._weaponEnabled.Clone();
        _tractorLockedOn = (bool[])source._tractorLockedOn.Clone();
        _projectiles.AddRange(source._projectiles.Select(static p => p.Clone()));
        _weaponNum = source._weaponNum;
        _pilotAccuracyBonus = source._pilotAccuracyBonus;
        _pilotDodgeBonus = source._pilotDodgeBonus;
        _pilotApproachSpeedModifier = source._pilotApproachSpeedModifier;
        _craftAccelerationBonus = source._craftAccelerationBonus;
        _missileCraft = source._missileCraft;
        _disableDisengage = source._disableDisengage;
        _disableStandoff = source._disableStandoff;
        _disableCautious = source._disableCautious;
        _disableStandard = source._disableStandard;
        _disableAggressive = source._disableAggressive;
        _ufoSize = source._ufoSize;
        _missileImpact = source._missileImpact;
        _endUfoHandled = source._endUfoHandled;
        _endCraftHandled = source._endCraftHandled;
        _destroyCraft = source._destroyCraft;
        _animatingHit = source._animatingHit;
        UfoIsAttacking = source.UfoIsAttacking;
        InterceptionNumber = source.InterceptionNumber;
        Mode = source.Mode;
        CurrentDistance = source.CurrentDistance;
        TargetDistance = source.TargetDistance;
        Timeout = source.Timeout;
        Status = source.Status;
        Minimized = source.Minimized;
        WaitForPoly = source.WaitForPoly;
        WaitForAltitude = source.WaitForAltitude;
        End = source.End;
        Ended = source.Ended;
        UfoBreakingOff = source.UfoBreakingOff;
        DestroyUfo = source.DestroyUfo;
        CraftDefenseless = source.CraftDefenseless;
        SelfDestructPressed = source.SelfDestructPressed;
        FiredAtLeastOnce = source.FiredAtLeastOnce;
        ExperienceAwarded = source.ExperienceAwarded;
        UfoBlobSize = source.UfoBlobSize;
    }

    /// <summary>An independent copy bound to copies of its craft and UFO, for a forecast of the next step.</summary>
    public Dogfight Clone(IDogfightWorld world, DogfightCraft craft, DogfightUfo ufo) => new(this, world, craft, ufo);

    public DogfightCraft Craft { get; }
    public DogfightUfo Ufo { get; }
    public bool UfoIsAttacking { get; }
    public int InterceptionNumber { get; private set; }

    /// <summary>GeoscapeState::startDogfight: the window's slot, given when it opens.</summary>
    public void AssignNumber(int number) => InterceptionNumber = number;
    public DogfightMode Mode { get; private set; }
    public int CurrentDistance { get; private set; }
    public int TargetDistance { get; private set; }
    public int Timeout { get; private set; }
    /// <summary>The status text key; empty once the timeout clears it.</summary>
    public string Status { get; private set; } = "";
    public bool Minimized { get; set; }
    public bool WaitForPoly { get; set; }
    public bool WaitForAltitude { get; set; }
    public bool End { get; private set; }
    public bool Ended { get; private set; }
    public bool UfoBreakingOff { get; private set; }
    public bool DestroyUfo { get; private set; }
    public bool DestroyCraft => _destroyCraft;
    public bool CraftDefenseless { get; private set; }
    public bool SelfDestructPressed { get; private set; }
    public bool FiredAtLeastOnce { get; private set; }
    /// <summary>DogfightState::_experienceAwarded, set by the world when it awards pilot experience.</summary>
    public bool ExperienceAwarded { get; set; }
    public int UfoBlobSize { get; private set; }
    public IReadOnlyList<DogfightProjectile> Projectiles => _projectiles;

    public int PilotAccuracyBonus => _pilotAccuracyBonus;
    public int PilotDodgeBonus => _pilotDodgeBonus;
    public int PilotApproachSpeedModifier => _pilotApproachSpeedModifier;
    public int CraftAccelerationBonus => _craftAccelerationBonus;
    public int UfoSize => _ufoSize;
    public int WeaponCount => _weaponNum;
    public bool WeaponEnabled(int index) => _weaponEnabled[index];
    public int WeaponFireCountdown(int index) => _weaponFireCountdown[index];
    public int WeaponFireInterval(int index) => _weaponFireInterval[index];

    /// <summary>Whether the reference shows the mode's button (hunter-killer and missile fights hide some).</summary>
    public bool ModeAvailable(DogfightMode mode) => mode switch
    {
        DogfightMode.Standoff => !_disableStandoff,
        DogfightMode.Cautious => !_disableCautious,
        DogfightMode.Standard => !_disableStandard,
        DogfightMode.Aggressive => !_disableAggressive,
        _ => !_disableDisengage,
    };

    private IRandomSource Random => _world.Random;

    private int DifficultyCoefficient => _world.Settings.DifficultyCoefficient(_world.Difficulty);

    private DogfightWeapon? Weapon(int index) => index < Craft.Weapons.Count ? Craft.Weapons[index] : null;

    private bool Percent(int chance) => RandomChance.Percent(Random, chance);

    /// <summary>Selects a mode as its radio button does, then runs its handler.</summary>
    public void SelectMode(DogfightMode mode)
    {
        Mode = mode;
        switch (mode)
        {
            case DogfightMode.Standoff: PressStandoff(); break;
            case DogfightMode.Cautious: PressCautious(); break;
            case DogfightMode.Standard: PressStandard(); break;
            case DogfightMode.Aggressive: PressAggressive(); break;
            default: PressDisengage(); break;
        }
    }

    /// <summary>DogfightState::think, without the start sound and recolouring.</summary>
    public void Think()
    {
        if (!Ended) Update();
        if (!UfoIsAttacking || Ufo.Status == DogfightUfoStatus.Landed)
        {
            if (!Craft.InDogfight || !_world.CraftTargetsUfo(Craft, Ufo) || Ufo.Status == DogfightUfoStatus.Landed)
                EndDogfight();
        }
    }

    /// <summary>DogfightState::endDogfight.</summary>
    public void EndDogfight()
    {
        if (Ended) return;
        Craft.InDogfight = false;
        Craft.InterceptionOrder = 0;
        Ufo.InterceptionProcessed = false;
        Ufo.ShieldRechargeHandle = 0;
        Ended = true;
    }

    /// <summary>DogfightState::update.</summary>
    public void Update()
    {
        var finalRun = false;
        if (!UfoIsAttacking)
        {
            if (!_world.CraftTargetsUfo(Craft, Ufo) || !Craft.InDogfight || Craft.LowFuel || Minimized && Ufo.IsCrashed)
            {
                EndDogfight();
                return;
            }
        }

        if (!Minimized)
        {
            Animate();
            if (!Ufo.IsCrashed && !Ufo.IsDestroyed && !Craft.IsDestroyed && !Ufo.InterceptionProcessed)
            {
                Ufo.InterceptionProcessed = true;
                var escapeCounter = Ufo.EscapeCountdown;
                if (UfoIsAttacking)
                {
                    if (_disableDisengage && Ufo.SoftlockShotCounter >= Ufo.Rule.SoftlockThreshold)
                    {
                        escapeCounter = 1;
                    }
                    else if (Ufo.Damage > Ufo.Stats.DamageMaximum / 3 && Ufo.HuntBehavior != 1)
                    {
                        escapeCounter = Craft.Damage > Craft.Stats.DamageMaximum / 2 ? 999 : 1;
                    }
                    else
                    {
                        escapeCounter = 999;
                    }
                }
                else if (_world.DogfightAI)
                {
                    var maxRange = 0;
                    foreach (var weapon in Craft.Weapons)
                    {
                        if (weapon is null || weapon.Ammo == 0) continue;
                        if (weapon.Rule.Range > maxRange) maxRange = weapon.Rule.Range;
                    }
                    var ufoSpeed = Math.Max(0, Ufo.Stats.SpeedMaximum - Ufo.TractorBeamSlowdown);
                    if (ufoSpeed > Craft.Stats.SpeedMaximum && CurrentDistance <= maxRange * 8 &&
                        TargetDistance > Ufo.Rule.WeaponRange * 8)
                    {
                        TargetDistance = Ufo.Rule.WeaponRange * 8;
                        SetStatus("STR_CANT_KEEP_DISTANCE");
                    }
                }

                if (escapeCounter > 0)
                {
                    escapeCounter--;
                    Ufo.EscapeCountdown = escapeCounter;
                    if (escapeCounter == 0)
                    {
                        Ufo.Speed = Ufo.Stats.SpeedMaximum;
                        if (UfoIsAttacking && Ufo.HunterKiller)
                        {
                            _world.ResetHunterDestination(Ufo, Craft);
                            Ufo.HunterKiller = false;
                        }
                    }
                }
                if (Ufo.FireCountdown > 0) Ufo.FireCountdown--;
            }
        }

        var speedMinusTractors = Math.Max(0, Ufo.Speed - Ufo.TractorBeamSlowdown);
        if (speedMinusTractors > Craft.Stats.SpeedMaximum)
        {
            if (!UfoIsAttacking || !Ufo.HunterKiller)
            {
                UfoBreakingOff = true;
                finalRun = true;
                SetStatus("STR_UFO_OUTRUNNING_INTERCEPTOR");
            }
        }
        else
        {
            UfoBreakingOff = false;
        }

        var projectileInFlight = false;
        if (!Minimized)
        {
            var distanceChange = 0;
            if (!UfoBreakingOff)
            {
                if (CurrentDistance < TargetDistance && !Ufo.IsCrashed && !Craft.IsDestroyed)
                {
                    distanceChange = 2 * _craftAccelerationBonus;
                    if (CurrentDistance + distanceChange > TargetDistance) distanceChange = TargetDistance - CurrentDistance;
                }
                else if (CurrentDistance > TargetDistance && !Ufo.IsCrashed && !Craft.IsDestroyed)
                {
                    distanceChange = -1 * _pilotApproachSpeedModifier;
                }
                foreach (var projectile in _projectiles)
                    if (!projectile.IsBeam && projectile.Direction == DogfightDirection.Up)
                        projectile.Position += distanceChange;
            }
            else
            {
                distanceChange = 4;
            }
            CurrentDistance += distanceChange;

            if (Ufo.ShieldRechargeHandle == 0) Ufo.ShieldRechargeHandle = InterceptionNumber;
            if (Ufo.Shield != 0 && InterceptionNumber == Ufo.ShieldRechargeHandle)
            {
                var total = Ufo.Stats.ShieldRecharge / 100;
                if (Percent(Ufo.Stats.ShieldRecharge % 100)) total++;
                Ufo.SetShield(Ufo.Shield + total);
            }
            if (Craft.Shield != 0)
            {
                var total = Craft.Stats.ShieldRecharge / 100;
                if (Percent(Craft.Stats.ShieldRecharge % 100)) total++;
                if (total != 0) Craft.SetShield(Craft.Shield + total);
            }

            if (_missileCraft && !_missileImpact && !Craft.IsDestroyed && CurrentDistance <= AggressiveDistance)
            {
                _missileImpact = true;
                Craft.SetDamage(Craft.Stats.DamageMaximum);
                var damage = Craft.MissilePower;
                var shieldDamage = 0;
                if (Ufo.Shield != 0)
                {
                    shieldDamage = damage;
                    damage = Math.Max(0, shieldDamage - Ufo.Shield) * Ufo.Stats.ShieldBleedThrough;
                    Ufo.SetShield(Ufo.Shield - shieldDamage);
                }
                damage = Math.Max(0, damage - Ufo.Stats.Armor);
                SetUfoDamage(Ufo.Damage + damage);
                _world.DogfightExperience();
                if (Ufo.IsCrashed)
                {
                    finalRun = false;
                    MarkShotDown();
                }
                StartHitAnimation();
                HitStatus(damage, shieldDamage);
            }

            foreach (var p in _projectiles)
            {
                p.Move();
                if (p.Direction == DogfightDirection.Up)
                {
                    if ((p.Position >= CurrentDistance || p.IsBeam && p.ToBeRemoved) && !Ufo.IsCrashed && !p.Missed)
                    {
                        var chanceToHit = (p.Accuracy * (100 + 300 / (5 - _ufoSize)) + 100) / 200;
                        chanceToHit -= Ufo.Stats.AvoidBonus;
                        chanceToHit += Craft.Stats.HitBonus;
                        chanceToHit += _pilotAccuracyBonus;
                        if (Percent(chanceToHit))
                        {
                            var power = p.Damage * (Craft.Stats.PowerBonus + 100) / 100;
                            var damage = p.DamageItem is { } item ? item.RandomDamage(power, Random) : Random.NextInclusive(power / 2, power);
                            var shieldDamage = 0;
                            if (Ufo.Shield != 0)
                            {
                                shieldDamage = damage * p.ShieldDamageModifier / 100;
                                damage = p.ShieldDamageModifier == 0 ? 0
                                    : Math.Max(0, shieldDamage - Ufo.Shield) * Ufo.Stats.ShieldBleedThrough / p.ShieldDamageModifier;
                                Ufo.SetShield(Ufo.Shield - shieldDamage);
                            }
                            damage = Math.Max(0, damage - Ufo.Stats.Armor);
                            SetUfoDamage(Ufo.Damage + damage);
                            _world.DogfightExperience();
                            if (Ufo.IsCrashed)
                            {
                                finalRun = false;
                                MarkShotDown();
                            }
                            StartHitAnimation();
                            HitStatus(damage, shieldDamage);
                            p.Remove();
                        }
                        else if (p.IsBeam)
                        {
                            p.Remove();
                        }
                        else
                        {
                            p.Missed = true;
                        }
                    }
                    if (!p.IsBeam && p.Position / 8 >= p.Range) p.Remove();
                    else if (!Ufo.IsCrashed) projectileInFlight = true;
                }
                else if (p.Direction == DogfightDirection.Down)
                {
                    if (!p.IsBeam || p.ToBeRemoved)
                    {
                        var chanceToHit = p.Accuracy;
                        chanceToHit -= Craft.Stats.AvoidBonus;
                        chanceToHit += Ufo.Stats.HitBonus;
                        chanceToHit -= _pilotDodgeBonus;
                        if (UfoIsAttacking && Mode == DogfightMode.Cautious) chanceToHit /= 2;
                        if (Percent(chanceToHit) || SelfDestructPressed)
                        {
                            var power = p.Damage * (Ufo.Stats.PowerBonus + 100) / 100;
                            var damage = Random.NextInclusive(0, power);
                            if (Craft.Shield != 0)
                            {
                                var bleedThrough = Math.Max(0, damage - Craft.Shield) * Craft.Stats.ShieldBleedThrough / 100;
                                Craft.SetShield(Craft.Shield - damage);
                                damage = bleedThrough;
                                SetStatus("STR_INTERCEPTOR_SHIELD_HIT");
                            }
                            damage = Math.Max(0, damage - Craft.Stats.Armor);
                            if (SelfDestructPressed) damage = Craft.Stats.DamageMaximum;
                            if (damage != 0)
                            {
                                Craft.SetDamage(Craft.Damage + damage);
                                SetStatus(_missileCraft ? "STR_MISSILE_DAMAGED" : "STR_INTERCEPTOR_DAMAGED");
                                if (Mode == DogfightMode.Cautious && Craft.DamagePercentage >= 50 && !UfoIsAttacking)
                                    TargetDistance = StandoffDistance;
                            }
                        }
                        p.Remove();
                    }
                }
            }

            _projectiles.RemoveAll(static p => p.ToBeRemoved || p.Missed && p.Position <= 0);

            if (_disableDisengage && !CraftDefenseless && !_missileCraft && _projectiles.Count == 0)
            {
                var hasNoAmmo = true;
                foreach (var weapon in Craft.Weapons)
                {
                    if (weapon is { Ammo: > 0 })
                    {
                        hasNoAmmo = false;
                        break;
                    }
                }
                if (hasNoAmmo) CraftDefenseless = true;
            }

            for (var i = 0; i < _weaponNum; ++i)
            {
                if (Weapon(i) is not { } w || _missileCraft) continue;
                var timer = _weaponFireCountdown[i];
                if (timer == 0 && CurrentDistance <= w.Rule.Range * 8 && w.Ammo > 0 && Mode != DogfightMode.Standoff &&
                    Mode != DogfightMode.Disengage && !Ufo.IsCrashed && !Craft.IsDestroyed)
                {
                    if (_weaponEnabled[i])
                    {
                        FireWeapon(i);
                        projectileInFlight = true;
                    }
                }
                else if (timer > 0)
                {
                    --_weaponFireCountdown[i];
                }

                if (w.Rule.TractorBeamPower != 0)
                {
                    if (CurrentDistance <= w.Rule.Range * 8 && Mode != DogfightMode.Standoff && Mode != DogfightMode.Disengage &&
                        !Ufo.IsCrashed && !Craft.IsDestroyed && _weaponEnabled[i])
                    {
                        if (!_tractorLockedOn[i])
                        {
                            _tractorLockedOn[i] = true;
                            Ufo.TractorBeamSlowdown += TractorChange(w);
                            SetStatus("STR_TRACTOR_BEAM_ENGAGED");
                        }
                    }
                    else if (_tractorLockedOn[i])
                    {
                        _tractorLockedOn[i] = false;
                        Ufo.TractorBeamSlowdown -= TractorChange(w);
                        SetStatus("STR_TRACTOR_BEAM_DISENGAGED");
                    }
                }

                if (w.Ammo == 0 && !projectileInFlight && !Craft.IsDestroyed)
                {
                    if (Mode == DogfightMode.Cautious && !UfoIsAttacking) MinimumDistance();
                    else if (Mode == DogfightMode.Standard) MaximumDistance();
                }
            }

            if (CurrentDistance <= Ufo.Rule.WeaponRange * 8 && !Ufo.IsCrashed && !Craft.IsDestroyed)
            {
                if (Ufo.ShootingAt == 0) Ufo.ShootingAt = InterceptionNumber;
                if (Ufo.ShootingAt == InterceptionNumber && Ufo.FireCountdown == 0) UfoFireWeapon();
            }
            else if (Ufo.ShootingAt == InterceptionNumber)
            {
                Ufo.ShootingAt = 0;
            }
        }

        if (End && ((CurrentDistance > 640 || Minimized) && (Mode == DogfightMode.Disengage || UfoBreakingOff) ||
            Timeout == 0 && (Ufo.IsCrashed || Craft.IsDestroyed)))
        {
            if (UfoBreakingOff)
            {
                _world.MoveUfo(Ufo);
                if (!UfoIsAttacking) _world.CraftPursueUfo(Craft, Ufo);
            }
            if (!_destroyCraft && (DestroyUfo || Mode == DogfightMode.Disengage))
            {
                var keepOriginalTarget = UfoIsAttacking && !_world.CraftTargetsUfo(Craft, Ufo);
                if (!keepOriginalTarget || Mode == DogfightMode.Disengage) _world.CraftReturnToBase(Craft);
                if (UfoIsAttacking && _world.CraftThink(Craft)) _world.StopHuntingCraft(Craft);
            }
            if (Ufo.IsCrashed) _world.ReturnUnlandableFollowers(Ufo);
            EndDogfight();
        }

        if (CurrentDistance > 640 && UfoBreakingOff) finalRun = true;

        if (!End)
        {
            if (_endCraftHandled)
            {
                finalRun = true;
            }
            else if (Craft.IsDestroyed)
            {
                if (!_missileCraft) SetStatus("STR_INTERCEPTOR_DESTROYED");
                else if (!_missileImpact) SetStatus("STR_MISSILE_DESTROYED");
                Timeout += 30;
                finalRun = true;
                _destroyCraft = true;
                _endCraftHandled = true;
                Ufo.ShootingAt = 0;
            }

            if (_endUfoHandled)
            {
                finalRun = true;
            }
            else if (Ufo.IsCrashed)
            {
                _endUfoHandled = true;
                var shotByThisCraft = ReferenceEquals(Ufo.ShotDownBy, Craft.Key);
                if (shotByThisCraft) Retaliate();

                if (Ufo.IsDestroyed)
                {
                    if (shotByThisCraft)
                    {
                        _world.AddXcomActivity(Ufo, Ufo.Rule.Score * 2);
                        SetStatus("STR_UFO_DESTROYED");
                    }
                    DestroyUfo = true;
                }
                else
                {
                    if (shotByThisCraft)
                    {
                        SetStatus("STR_UFO_CRASH_LANDS");
                        _world.AddXcomActivity(Ufo, Ufo.Rule.Score);
                    }
                    var survived = true;
                    var fakeUnderwater = _world.InsideFakeUnderwater(Ufo);
                    if (!_world.InsideLand(Ufo))
                    {
                        survived = false;
                    }
                    else if (fakeUnderwater)
                    {
                        if (Percent(Ufo.Rule.SplashdownSurvivalChance))
                        {
                            SetStatus("STR_UFO_SURVIVED_SPLASHDOWN");
                        }
                        else
                        {
                            survived = false;
                            SetStatus("STR_UFO_DESTROYED_BY_SPLASHDOWN");
                        }
                    }
                    if (!survived)
                    {
                        Ufo.Status = DogfightUfoStatus.Destroyed;
                        DestroyUfo = true;
                    }
                    else
                    {
                        Ufo.SecondsRemaining = Random.NextInclusive(24, 96) * 3600;
                        Ufo.Altitude = WorldAltitudes.Ground;
                        if (Ufo.CrashId == 0)
                        {
                            Ufo.CrashId = _world.NextId("STR_CRASH_SITE");
                            if (Ufo.HunterKiller)
                            {
                                _world.ResetHunterDestination(Ufo, Craft);
                                Ufo.HunterKiller = false;
                            }
                        }
                    }
                }
                Timeout += 30;
                if (!shotByThisCraft)
                {
                    Timeout += 50;
                    Ufo.HitFrame = 3;
                }
                finalRun = true;
                if (Ufo.Status == DogfightUfoStatus.Landed)
                {
                    Timeout += 30;
                    finalRun = true;
                    Ufo.ShootingAt = 0;
                }
            }
            else if (Ufo.Stats.SpeedMaximum - Ufo.TractorBeamSlowdown == 0)
            {
                _endUfoHandled = true;
                var survived = true;
                if (!_world.InsideLand(Ufo))
                {
                    survived = false;
                }
                else if (_world.InsideFakeUnderwater(Ufo) && !Percent(Ufo.Rule.SplashdownSurvivalChance))
                {
                    survived = false;
                }
                if (Ufo.Rule.Unmanned) survived = false;
                if (!survived)
                {
                    finalRun = true;
                    SetUfoDamage(Ufo.Stats.DamageMaximum);
                    _world.DogfightExperience();
                    Ufo.ShotDownBy = Craft.Key;
                    Ufo.Speed = 0;
                    Ufo.Status = DogfightUfoStatus.Destroyed;
                    DestroyUfo = true;
                    _world.AddXcomActivity(Ufo, Ufo.Rule.Score);
                }
                else
                {
                    finalRun = true;
                    Ufo.SecondsRemaining = Random.NextInclusive(30, 120) * 60;
                    Ufo.ShootingAt = 0;
                    Ufo.Status = DogfightUfoStatus.Landed;
                    Ufo.Altitude = WorldAltitudes.Ground;
                    Ufo.Speed = 0;
                    Ufo.TractorBeamSlowdown = 0;
                    if (Ufo.LandId == 0) Ufo.LandId = _world.NextId("STR_LANDING_SITE");
                }
            }
        }

        if (!projectileInFlight && finalRun) End = true;
    }

    /// <summary>The craft's minimize button: a defenceless craft toggles self-destruct instead
    /// (DogfightState::btnMinimizeClick). Returns whether the window was minimized.</summary>
    public bool PressMinimize()
    {
        if (CraftDefenseless)
        {
            SelfDestructPressed = !SelfDestructPressed;
            SetStatus(SelfDestructPressed ? "STR_SELF_DESTRUCT_ACTIVATED" : "STR_SELF_DESTRUCT_CANCELLED");
            return false;
        }
        if (Ufo.IsCrashed || Craft.IsDestroyed || UfoBreakingOff) return false;
        if (CurrentDistance >= StandoffDistance)
        {
            Minimized = true;
            Ufo.ShieldRechargeHandle = 0;
            return true;
        }
        SetStatus("STR_MINIMISE_AT_STANDOFF_RANGE_ONLY");
        return false;
    }

    /// <summary>DogfightState::weaponClick: toggles a weapon, remembered on the weapon when the option is on.</summary>
    public void ToggleWeapon(int index, bool rememberDisabled)
    {
        _weaponEnabled[index] = !_weaponEnabled[index];
        if (rememberDisabled && Weapon(index) is { } w) w.Disabled = !_weaponEnabled[index];
    }

    private void PressStandoff()
    {
        if (Ufo.IsCrashed || Craft.IsDestroyed || UfoBreakingOff) return;
        End = false;
        SetStatus("STR_STANDOFF");
        TargetDistance = StandoffDistance;
    }

    private void PressCautious()
    {
        if (Ufo.IsCrashed || Craft.IsDestroyed || UfoBreakingOff) return;
        End = false;
        if (!UfoIsAttacking)
        {
            SetStatus("STR_CAUTIOUS_ATTACK");
            for (var i = 0; i < _weaponNum; ++i)
                if (Weapon(i) is { } w) _weaponFireInterval[i] = w.Rule.ReloadCautious;
            MinimumDistance();
        }
        else
        {
            SetStatus("STR_EVASIVE_MANEUVERS");
            for (var i = 0; i < _weaponNum; ++i)
                if (Weapon(i) is { } w) _weaponFireInterval[i] = w.Rule.ReloadAggressive * 2;
            AggressiveDistanceMode();
        }
    }

    private void PressStandard()
    {
        if (Ufo.IsCrashed || Craft.IsDestroyed || UfoBreakingOff) return;
        End = false;
        SetStatus("STR_STANDARD_ATTACK");
        for (var i = 0; i < _weaponNum; ++i)
            if (Weapon(i) is { } w) _weaponFireInterval[i] = w.Rule.ReloadStandard;
        MaximumDistance();
    }

    private void PressAggressive()
    {
        if (Ufo.IsCrashed || Craft.IsDestroyed || UfoBreakingOff) return;
        End = false;
        SetStatus("STR_AGGRESSIVE_ATTACK");
        for (var i = 0; i < _weaponNum; ++i)
            if (Weapon(i) is { } w) _weaponFireInterval[i] = w.Rule.ReloadAggressive;
        AggressiveDistanceMode();
    }

    private void PressDisengage()
    {
        if (Ufo.IsCrashed || Craft.IsDestroyed || UfoBreakingOff) return;
        End = true;
        SetStatus("STR_DISENGAGING");
        TargetDistance = 800;
    }

    /// <summary>DogfightState::animate: only its gameplay counters (status timeout, UFO hit frame).</summary>
    private void Animate()
    {
        if (Timeout == 0) Status = "";
        else Timeout--;

        var lastHitAnimationFrame = false;
        if (_animatingHit && Ufo.HitFrame > 0)
        {
            Ufo.HitFrame--;
            if (Ufo.HitFrame == 0)
            {
                _animatingHit = false;
                lastHitAnimationFrame = true;
            }
        }
        if (Ufo.IsCrashed && Ufo.HitFrame == 0 && !lastHitAnimationFrame) --UfoBlobSize;
    }

    private void SetStatus(string status)
    {
        Status = status;
        Timeout = TimeoutAfterStatus;
    }

    private void FireWeapon(int i)
    {
        var w = Weapon(i)!;
        if (!w.SetAmmo(w.Ammo - 1)) return;
        _weaponFireCountdown[i] = _weaponFireInterval[i];
        var p = new DogfightProjectile(w.Rule.UnifiedDamage);
        p.SetType(w.Rule.ProjectileType);
        p.SetSpeed(w.Rule.ProjectileSpeed);
        p.Accuracy = w.Rule.Accuracy;
        p.Damage = w.Rule.Damage;
        p.Range = w.Rule.Range;
        p.ShieldDamageModifier = w.Rule.ShieldDamageModifier;
        p.SetDirection(DogfightDirection.Up);
        p.HorizontalPosition = (i % 2 != 0 ? DogfightProjectile.HorizontalRight : DogfightProjectile.HorizontalLeft) * (1 + 2 * (i / 2));
        _projectiles.Add(p);
        FiredAtLeastOnce = true;
    }

    private void UfoFireWeapon()
    {
        var fireCountdown = Math.Max(1, Ufo.Rule.WeaponReload - 2 * DifficultyCoefficient);
        var custom = _world.Settings.UfoFiringRateCoefficients;
        if (custom.Count > _world.Difficulty)
            fireCountdown = Math.Max(1, Ufo.Rule.WeaponReload * custom[_world.Difficulty] / 100);
        Ufo.FireCountdown = Random.NextInclusive(0, fireCountdown) + fireCountdown;

        SetStatus("STR_UFO_RETURN_FIRE");
        var p = new DogfightProjectile(null);
        p.SetType(DogfightProjectileType.PlasmaBeam);
        p.Accuracy = 60;
        p.Damage = Ufo.Rule.WeaponPower;
        p.SetDirection(DogfightDirection.Down);
        p.HorizontalPosition = DogfightProjectile.HorizontalCenter;
        p.Position = CurrentDistance - Ufo.Rule.Radius / 2;
        _projectiles.Add(p);
        if (UfoIsAttacking && _disableDisengage) Ufo.SoftlockShotCounter++;
    }

    private int TractorChange(DogfightWeapon weapon) =>
        weapon.Rule.TractorBeamPower * _world.Settings.UfoTractorBeamSizeModifiers[_ufoSize] / 100;

    private void MinimumDistance()
    {
        var max = 0;
        foreach (var weapon in Craft.Weapons)
        {
            if (weapon is null) continue;
            if (weapon.Rule.Range > max && weapon.Ammo > 0) max = weapon.Rule.Range;
        }
        TargetDistance = max == 0 ? StandoffDistance : max * 8;
    }

    private void MaximumDistance()
    {
        var min = 1000;
        foreach (var weapon in Craft.Weapons)
        {
            if (weapon is null) continue;
            if (weapon.Rule.Range < min && weapon.Ammo > 0) min = weapon.Rule.Range;
        }
        if (UfoIsAttacking && Ufo.Rule.WeaponRange > 0 && Ufo.Rule.WeaponRange < min) min = Ufo.Rule.WeaponRange;
        TargetDistance = min == 1000 ? StandoffDistance : min * 8;
    }

    private void AggressiveDistanceMode()
    {
        MaximumDistance();
        if (TargetDistance > AggressiveDistance) TargetDistance = AggressiveDistance;
    }

    /// <summary>Ufo::setDamage: crash or destruction status, then the wave's interrupt roll.</summary>
    private void SetUfoDamage(int damage)
    {
        Ufo.Damage = Math.Max(0, damage);
        if (Ufo.IsDestroyed) Ufo.Status = DogfightUfoStatus.Destroyed;
        else if (Ufo.IsCrashed) Ufo.Status = DogfightUfoStatus.Crashed;
        if (Ufo.Status is DogfightUfoStatus.Crashed or DogfightUfoStatus.Destroyed)
        {
            var interrupt = _world.WaveInterruptPercentage(Ufo);
            if (interrupt > 0 && Percent(interrupt)) _world.InterruptMission(Ufo);
        }
    }

    /// <summary>The crash bookkeeping shared by missile impacts and projectile hits.</summary>
    private void MarkShotDown()
    {
        Ufo.ShotDownBy = Craft.Key;
        Ufo.Speed = 0;
        _world.ClearUfoDestination(Ufo);
        UfoBreakingOff = false;
        End = false;
    }

    private void StartHitAnimation()
    {
        if (Ufo.HitFrame != 0) return;
        _animatingHit = true;
        Ufo.HitFrame = 3;
    }

    private void HitStatus(int damage, int shieldDamage)
    {
        if (Ufo.Shield != 0) SetStatus("STR_UFO_SHIELD_HIT");
        else if (damage == 0) SetStatus(shieldDamage == 0 ? "STR_UFO_HIT_NO_DAMAGE" : "STR_UFO_SHIELD_DOWN");
        else if (damage < Ufo.Stats.DamageMaximum / 2 * _world.Settings.UfoGlancingHitThreshold / 100) SetStatus("STR_UFO_HIT_GLANCING");
        else SetStatus("STR_UFO_HIT");
    }

    /// <summary>The retaliation check of a UFO shot down by this craft.</summary>
    private void Retaliate()
    {
        _world.UfoShotDown(Ufo);
        var retaliationOdds = _world.MissionRetaliationOdds(Ufo);
        if (retaliationOdds == -1)
        {
            var aggression = _world.RetaliationAggression(Ufo);
            retaliationOdds = 100 - (4 * (24 - DifficultyCoefficient) - aggression);
            var custom = _world.Settings.RetaliationTriggerOdds;
            if (custom.Count > _world.Difficulty) retaliationOdds = custom[_world.Difficulty] + aggression;
        }
        if (_world.MonthsPassed < _world.Settings.DifficultyBasedRetaliationDelay[_world.Difficulty]) retaliationOdds = 0;
        if (!Percent(retaliationOdds)) return;

        var regionOdds = 50 - 6 * DifficultyCoefficient;
        var customRegion = _world.Settings.RetaliationBaseRegionOdds;
        if (customRegion.Count > _world.Difficulty) regionOdds = 100 - customRegion[_world.Difficulty];
        var targetRegion = Percent(regionOdds) ? _world.UfoMissionRegion(Ufo) : _world.CraftBaseRegion(Craft);
        if (!_world.RetaliationMissionExists(targetRegion, Ufo)) _world.StartRetaliation(targetRegion, Ufo);
    }

    // Craft::getPilotAccuracyBonus, getPilotDodgeBonus and getPilotApproachSpeedModifier. The average
    // divides by size_t, so a negative sum wraps like the reference's int / size_t conversion.
    private static int Average(IReadOnlyList<DogfightPilot> pilots, Func<DogfightPilot, int> stat)
    {
        var sum = 0;
        foreach (var pilot in pilots) sum += stat(pilot);
        return unchecked((int)((ulong)(long)sum / (ulong)pilots.Count));
    }

    private static int AccuracyBonus(IReadOnlyList<DogfightPilot> pilots, Oxce.Mods.Rulesets.CampaignStart.CampaignInterceptionSettings settings) =>
        pilots.Count == 0 ? 0 : (Average(pilots, static p => p.Firing) - settings.PilotAccuracyZeroPoint) * settings.PilotAccuracyRange / 100;

    private static int DodgeBonus(IReadOnlyList<DogfightPilot> pilots, Oxce.Mods.Rulesets.CampaignStart.CampaignInterceptionSettings settings) =>
        pilots.Count == 0 ? 0 : (Average(pilots, static p => p.Reactions) - settings.PilotReactionsZeroPoint) * settings.PilotReactionsRange / 100;

    private static int ApproachSpeedModifier(IReadOnlyList<DogfightPilot> pilots, Oxce.Mods.Rulesets.CampaignStart.CampaignInterceptionSettings settings)
    {
        if (pilots.Count == 0) return 2;
        var bravery = Average(pilots, static p => p.Bravery);
        var thresholds = settings.PilotBraveryThresholds;
        if (bravery >= thresholds[0]) return 4;
        if (bravery >= thresholds[1]) return 3;
        if (bravery >= thresholds[2]) return 2;
        return 1;
    }
}
