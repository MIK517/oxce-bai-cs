using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns.World.Interception;
using Oxce.Mods.Rulesets.CampaignStart;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>The interception modes (the dogfight window's mode buttons).</summary>
public enum InterceptionMode
{
    Standoff,
    Cautious,
    Standard,
    Aggressive,
    Disengage,
}

/// <summary>One dogfight timer step for every open interception (GeoscapeState::handleDogfights).</summary>
public sealed record AdvanceInterceptions : ICampaignCommand;

/// <summary>Presses a mode button of an interception window.</summary>
public sealed record SetInterceptionMode(int Interception, InterceptionMode Mode) : ICampaignCommand;

/// <summary>The window's minimize button; a defenceless craft toggles self-destruct instead.</summary>
public sealed record MinimizeInterception(int Interception) : ICampaignCommand;

/// <summary>Reopens a minimized interception window.</summary>
public sealed record RestoreInterception(int Interception) : ICampaignCommand;

/// <summary>Toggles one craft weapon of an interception (DogfightState::weaponClick).</summary>
public sealed record ToggleInterceptionWeapon(int Interception, int Slot) : ICampaignCommand;

public sealed record InterceptionEnded(int Interception, int BaseId, string CraftTypeId, int CraftId, int UfoUniqueId) : ICampaignEvent;

public sealed record CampaignInterceptionWeapon(int Slot, string RuleId, int Ammo, bool Enabled);

/// <summary>An open interception window as the UI shows it.</summary>
public sealed record CampaignInterception(
    int Number,
    int BaseId,
    string CraftTypeId,
    int CraftId,
    int UfoUniqueId,
    bool UfoAttacking,
    InterceptionMode Mode,
    int Distance,
    int TargetDistance,
    string Status,
    bool Minimized,
    bool CraftDefenseless,
    bool SelfDestructPressed,
    IReadOnlyList<InterceptionMode> AvailableModes,
    IReadOnlyList<CampaignInterceptionWeapon> Weapons);

public interface ICampaignInterceptionQuery
{
    IReadOnlyList<CampaignInterception> QueryInterceptions();
}

/// <summary>
/// Open interceptions: the dogfight windows of <c>GeoscapeState</c> and their step. Combat state is
/// transient as in the reference: saves carry only the craft and UFO fields, so a reload starts without
/// interceptions. A step is forecast on copies first; outcomes whose world consequences are not
/// implemented yet stop the step before anything changes. Reference: <c>GeoscapeState::handleDogfights</c>,
/// <c>DogfightState</c> and <c>Craft::getPilotList</c> at 4df3a5e.
/// </summary>
internal sealed partial class CampaignWorld : ICampaignInterceptionQuery
{
    private readonly List<Dogfight> _interceptions = [];
    // Ufo objects outlive their dogfights in the reference: the tractor-beam slowdown, firing window and
    // hit frame stay with the UFO until it is removed or the game is reloaded.
    private readonly Dictionary<int, DogfightUfo> _interceptionUfos = [];

    private sealed record InterceptionCraftKey(int BaseId, string TypeId, int Id);

    private CampaignState Campaign => campaign;

    private void RegisterInterceptions(CampaignCapabilityRegistry registry)
    {
        registry.Query<ICampaignInterceptionQuery>(this);
        // GeoscapeState::think runs the clock only while every window is minimized.
        registry.Preflight(CampaignPreflightOrder.Interceptions, "interceptions",
            (_, _) => _interceptions.Any(static d => !d.Minimized) ? WorldCommandReasons.InterceptionsOpen : null);
        RegisterInterceptionStart(registry);
        RegisterCraftLosses(registry);
        RegisterLandings(registry);
        RegisterBaseAttacks(registry);
        registry.Command<AdvanceInterceptions>(_ => AdvanceInterceptionStep());
        registry.Command<SetInterceptionMode>(SetInterceptionMode);
        registry.Command<MinimizeInterception>(MinimizeInterception);
        registry.Command<RestoreInterception>(RestoreInterception);
        registry.Command<ToggleInterceptionWeapon>(ToggleInterceptionWeapon);
    }

    /// <summary>Whether the craft is in an open interception (Craft::isInDogfight).</summary>
    internal bool CraftInDogfight(int baseId, string craftTypeId, int craftId) =>
        OpenAndPendingInterceptions.Any(d => d.Craft.InDogfight && d.Craft.Key is InterceptionCraftKey key &&
            key.BaseId == baseId && key.TypeId == craftTypeId && key.Id == craftId);

    internal IReadOnlyList<Dogfight> Interceptions => _interceptions;

    /// <summary>The defenses and grav shields a UFO attacking the base meets (branch 5 S7).</summary>
    internal (IReadOnlyList<BaseDefenseWeapon> Defenses, int GravShields) BaseDefenses(int baseId) => campaign.Read(() =>
        BaseDefenseSetup.For(campaign.BaseStates.Single(owner => owner.Id == baseId), campaign.Content.RuntimeRules));

    /// <summary>
    /// Opens an interception window (the DogfightState constructor plus GeoscapeState::startDogfight's
    /// slot numbering). Returns the interception number.
    /// </summary>
    internal int StartInterception(CampaignState.BaseState owner, int craftIndex, int ufoUniqueId, bool ufoIsAttacking)
    {
        var ufoIndex = UfoIndex(ufoUniqueId);
        if (ufoIndex < 0) throw new ArgumentOutOfRangeException(nameof(ufoUniqueId));
        var number = FirstFreeInterceptionSlot();
        var world = new InterceptionWorld(this, forecast: false, _interceptions);
        var ufo = InterceptionUfo(_ufos[ufoIndex]);
        var craft = InterceptionCraft(owner, craftIndex);
        var dogfight = new Dogfight(world, craft, ufo, ufoIsAttacking, number);
        _interceptions.Add(dogfight);
        WriteBack(dogfight);
        return number;
    }

    // GeoscapeState::getFirstFreeDogfightSlot, including its single ordered pass.
    private int FirstFreeInterceptionSlot()
    {
        var slot = 1;
        foreach (var dogfight in _interceptions)
            if (dogfight.InterceptionNumber == slot) ++slot;
        return slot;
    }

    public IReadOnlyList<CampaignInterception> QueryInterceptions() => campaign.Read(() =>
        (IReadOnlyList<CampaignInterception>)[.. _interceptions.Select(QueryInterception)]);

    private CampaignInterception QueryInterception(Dogfight d)
    {
        var key = (InterceptionCraftKey)d.Craft.Key;
        var state = FindInterceptionCraft(key).Craft.Logistics!;
        var weapons = new List<CampaignInterceptionWeapon>();
        for (var slot = 0; slot < Math.Min(d.WeaponCount, state.Weapons.Count); slot++)
            if (state.Weapons[slot] is { } weapon) weapons.Add(new(slot, weapon.RuleId, weapon.Ammo, d.WeaponEnabled(slot)));
        return new(d.InterceptionNumber, key.BaseId, key.TypeId, key.Id, (int)d.Ufo.Key, d.UfoIsAttacking, (InterceptionMode)d.Mode,
            d.CurrentDistance, d.TargetDistance, d.Status, d.Minimized, d.CraftDefenseless, d.SelfDestructPressed,
            [.. Enum.GetValues<InterceptionMode>().Where(mode => d.ModeAvailable((DogfightMode)mode))], weapons);
    }

    private CampaignCommandResult AdvanceInterceptionStep()
    {
        if (_interceptions.Count == 0) return new CampaignCommandResult([]);
        RefreshInterceptionMirrors();

        // Forecast the whole step on copies; the random source is rewound afterwards so the real step
        // draws the same values.
        var random = campaign.Random;
        var state = random.State;
        var ufoCopies = new Dictionary<object, DogfightUfo>();
        var copies = new List<Dogfight>();
        var forecast = new InterceptionWorld(this, forecast: true, copies);
        foreach (var dogfight in _interceptions)
        {
            if (!ufoCopies.TryGetValue(dogfight.Ufo.Key, out var ufo)) ufoCopies.Add(dogfight.Ufo.Key, ufo = dogfight.Ufo.Clone());
            copies.Add(dogfight.Clone(forecast, dogfight.Craft.Clone(), ufo));
        }
        Step(copies);
        random.Restore(state);
        if (forecast.Reason is { } reason) return Blocked(reason);

        var events = new List<ICampaignEvent>();
        var ended = Step(_interceptions);
        foreach (var dogfight in _interceptions) WriteBack(dogfight);
        foreach (var dogfight in ended)
        {
            var key = (InterceptionCraftKey)dogfight.Craft.Key;
            events.Add(new InterceptionEnded(dogfight.InterceptionNumber, key.BaseId, key.TypeId, key.Id, (int)dogfight.Ufo.Key));
            WriteBack(dogfight);
            _interceptions.Remove(dogfight);
        }
        return new CampaignCommandResult(events);
    }

    // GeoscapeState::handleDogfights over the given windows; returns the windows that ended.
    private List<Dogfight> Step(List<Dogfight> windows)
    {
        foreach (var dogfight in windows) dogfight.Ufo.InterceptionProcessed = false;
        var ended = new List<Dogfight>();
        foreach (var dogfight in windows)
        {
            if (dogfight.Minimized)
            {
                if (dogfight.WaitForPoly && InsideLand(dogfight.Ufo))
                {
                    dogfight.Minimized = false;
                    dogfight.WaitForPoly = false;
                }
                else if (dogfight.WaitForAltitude && WorldAltitudes.Index(dogfight.Ufo.Altitude) <= CraftMaximumAltitude(dogfight.Craft))
                {
                    dogfight.Minimized = false;
                    dogfight.WaitForAltitude = false;
                }
            }
            dogfight.Think();
            if (dogfight.Ended) ended.Add(dogfight);
        }
        return ended;
    }

    private CampaignCommandResult SetInterceptionMode(SetInterceptionMode command)
    {
        if (FindInterception(command.Interception) is not { } dogfight) return Blocked(WorldCommandReasons.NoInterception);
        var mode = (DogfightMode)command.Mode;
        if (!Enum.IsDefined(command.Mode) || !dogfight.ModeAvailable(mode) || dogfight.Minimized)
            return Blocked(WorldCommandReasons.InterceptionMode);
        RefreshInterceptionMirrors();
        dogfight.SelectMode(mode);
        return new CampaignCommandResult([]);
    }

    private CampaignCommandResult MinimizeInterception(MinimizeInterception command)
    {
        if (FindInterception(command.Interception) is not { } dogfight || dogfight.Minimized) return Blocked(WorldCommandReasons.NoInterception);
        // The minimize button is hidden while a hunter-killer attacks, unless it became the self-destruct switch.
        if (dogfight.UfoIsAttacking && !dogfight.CraftDefenseless) return Blocked(WorldCommandReasons.InterceptionMode);
        RefreshInterceptionMirrors();
        dogfight.PressMinimize();
        WriteBack(dogfight);
        return new CampaignCommandResult([]);
    }

    private CampaignCommandResult RestoreInterception(RestoreInterception command)
    {
        if (FindInterception(command.Interception) is not { Minimized: true } dogfight) return Blocked(WorldCommandReasons.NoInterception);
        // DogfightState::btnMinimizedIconClick: water-only craft wait for depth or polygons (branch 5 S4).
        if (CraftMaximumAltitude(dogfight.Craft) > -1) return Blocked(WorldCommandReasons.InterceptionBoundary);
        dogfight.Minimized = false;
        return new CampaignCommandResult([]);
    }

    private CampaignCommandResult ToggleInterceptionWeapon(ToggleInterceptionWeapon command)
    {
        if (FindInterception(command.Interception) is not { Minimized: false } dogfight ||
            command.Slot < 0 || command.Slot >= Math.Min(dogfight.WeaponCount, dogfight.Craft.Weapons.Count) ||
            dogfight.Craft.Weapons[command.Slot] is null)
            return Blocked(WorldCommandReasons.NoInterception);
        RefreshInterceptionMirrors();
        dogfight.ToggleWeapon(command.Slot, campaign.Options.RememberDisabledCraftWeapons);
        WriteBack(dogfight);
        return new CampaignCommandResult([]);
    }

    private Dogfight? FindInterception(int number) => _interceptions.FirstOrDefault(d => d.InterceptionNumber == number);

    // ---- Mirrors ----------------------------------------------------------------------------------

    private void RefreshInterceptionMirrors()
    {
        foreach (var dogfight in _interceptions)
        {
            var (owner, craft) = FindInterceptionCraft((InterceptionCraftKey)dogfight.Craft.Key);
            var state = craft.Logistics!;
            dogfight.Craft.Damage = state.Damage;
            dogfight.Craft.Shield = state.Shield;
            dogfight.Craft.LowFuel = state.LowFuel;
            dogfight.Craft.InterceptionOrder = state.InterceptionOrder;
            for (var slot = 0; slot < dogfight.Craft.Weapons.Count; slot++)
            {
                if (dogfight.Craft.Weapons[slot] is not { } weapon || state.Weapons[slot] is not { } saved) continue;
                weapon.SetAmmo(saved.Ammo);
                weapon.Disabled = saved.Disabled;
            }
            _ = owner;
        }
        foreach (var uniqueId in _interceptionUfos.Keys.ToArray())
        {
            var index = UfoIndex(uniqueId);
            if (index >= 0) Refresh(_interceptionUfos[uniqueId], _ufos[index]);
            else if (!_interceptions.Any(d => (int)d.Ufo.Key == uniqueId)) _interceptionUfos.Remove(uniqueId);
        }
    }

    private DogfightUfo InterceptionUfo(UfoSnapshot snapshot)
    {
        if (_interceptionUfos.TryGetValue(snapshot.UniqueId, out var existing))
        {
            Refresh(existing, snapshot);
            return existing;
        }
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Ufos[rules.Ufos.GetRequired(snapshot.RuleId)].Value;
        var stats = rule.StatsForRace(FindMission(snapshot.MissionId)?.Race ?? "");
        var ufo = new DogfightUfo
        {
            Key = snapshot.UniqueId,
            Rule = new DogfightUfoRule(rule.WeaponRange, rule.WeaponPower, rule.WeaponReload, rule.Radius, rule.Score, rule.BreakOffTime,
                rule.SoftlockThreshold, rule.SplashdownSurvivalChance, rule.Unmanned, rule.BlobSize),
            Stats = new DogfightStats(stats.DamageMaximum, stats.SpeedMaximum, stats.Acceleration, stats.HitBonus, stats.AvoidBonus,
                stats.PowerBonus, stats.Armor, stats.ShieldCapacity, stats.ShieldRecharge, stats.ShieldBleedThrough),
        };
        Refresh(ufo, snapshot);
        _interceptionUfos.Add(snapshot.UniqueId, ufo);
        return ufo;
    }

    private void Refresh(DogfightUfo ufo, UfoSnapshot snapshot)
    {
        ufo.TargetCraft = snapshot.Hunting && snapshot.Destination is { Kind: WorldTargetKind.Craft } craft ? CraftKeyFor(craft) : null;
        ufo.Longitude = snapshot.Longitude;
        ufo.Latitude = snapshot.Latitude;
        ufo.Damage = snapshot.Damage;
        ufo.Shield = snapshot.Shield;
        ufo.Speed = snapshot.Speed;
        ufo.EscapeCountdown = snapshot.EscapeCountdown;
        ufo.FireCountdown = snapshot.FireCountdown;
        ufo.ShieldRechargeHandle = snapshot.ShieldRechargeHandle;
        ufo.SoftlockShotCounter = snapshot.SoftlockShotCounter;
        ufo.HunterKiller = snapshot.HunterKiller;
        ufo.HuntBehavior = snapshot.HuntBehavior;
        ufo.Status = snapshot.Status switch
        {
            UfoStatus.Landed => DogfightUfoStatus.Landed,
            UfoStatus.Crashed => DogfightUfoStatus.Crashed,
            UfoStatus.Destroyed => DogfightUfoStatus.Destroyed,
            _ => DogfightUfoStatus.Flying,
        };
        ufo.SecondsRemaining = snapshot.SecondsRemaining;
        ufo.Altitude = snapshot.Altitude;
        ufo.CrashId = snapshot.CrashId;
        ufo.LandId = snapshot.LandId;
    }

    private InterceptionCraftKey? CraftKeyFor(WorldTargetReference craft)
    {
        foreach (var owner in campaign.BaseStates)
            foreach (var candidate in owner.Crafts)
                if (candidate.Id == craft.Id && campaign.Content.RuntimeRules.Crafts.GetExternalId(candidate.Rule) == craft.TypeId)
                    return new(owner.Id, craft.TypeId, craft.Id);
        return null;
    }

    private DogfightCraft InterceptionCraft(CampaignState.BaseState owner, int craftIndex)
    {
        var craft = owner.Crafts[craftIndex];
        var state = craft.Logistics ?? throw new InvalidOperationException("An intercepting craft needs logistics state.");
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Crafts[craft.Rule].Value;
        var type = rules.Crafts.GetExternalId(craft.Rule);
        long Stat(string key)
        {
            long value = rule.DefaultStats.GetValueOrDefault(key);
            foreach (var weapon in state.Weapons)
                if (weapon is not null) value += rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value.BonusStats.GetValueOrDefault(key);
            return value;
        }
        int Bounded(string key) => (int)Math.Clamp(Stat(key), int.MinValue, int.MaxValue);
        var weapons = new DogfightWeapon?[state.Weapons.Count];
        for (var slot = 0; slot < weapons.Length; slot++)
        {
            if (state.Weapons[slot] is not { } saved) continue;
            var weaponRule = rules.CraftWeapons[rules.CraftWeapons.GetRequired(saved.RuleId)].Value;
            IDogfightDamageType? unified = null;
            if (weaponRule.UnifiedDamageFormula)
            {
                var itemId = weaponRule.Clip.Length != 0 ? weaponRule.Clip : weaponRule.Launcher;
                var damage = rules.Items[rules.Items.GetRequired(itemId)].Value.Damage;
                unified = new DogfightItemDamage(damage.RandomType, damage.ResistType, rules.Campaign.Interception);
            }
            weapons[slot] = new DogfightWeapon(new DogfightWeaponRule(weaponRule.Damage, weaponRule.ShieldDamageModifier, weaponRule.Range,
                weaponRule.Accuracy, weaponRule.ReloadCautious, weaponRule.ReloadStandard, weaponRule.ReloadAggressive, weaponRule.AmmoMaximum,
                (DogfightProjectileType)weaponRule.ProjectileType, weaponRule.ProjectileSpeed, weaponRule.TractorBeamPower)
            { UnifiedDamage = unified }, saved.Ammo, saved.Disabled);
        }
        var (pilots, pilotIds) = PilotList(owner, craft, rule, type, state);
        // Craft::getPilotList leaves _pilots alone when the craft has no pilot seats.
        if (rule.Pilots != 0) owner.Crafts[craftIndex] = craft with { Logistics = state with { Pilots = pilotIds } };
        return new DogfightCraft
        {
            Key = new InterceptionCraftKey(owner.Id, type, craft.Id),
            Stats = new DogfightStats(Bounded("damageMax"), Bounded("speedMax"), Bounded("accel"), Bounded("hitBonus"), Bounded("avoidBonus"),
                Bounded("powerBonus"), Bounded("armor"), Bounded("shieldCapacity"), Bounded("shieldRecharge"), Bounded("shieldBleedThrough")),
            Damage = state.Damage,
            Shield = state.Shield,
            LowFuel = state.LowFuel,
            InterceptionOrder = state.InterceptionOrder,
            MissilePower = rule.MissilePower,
            AllowLanding = rule.AllowLanding,
            WeaponSlots = rule.WeaponSlots,
            Weapons = weapons,
            Pilots = [.. pilots.Select(soldier => PilotStats(soldier.Personal!))],
        };
    }

    /// <summary>
    /// Craft::getPilotList(false): every crew member who meets the piloting requirements when they
    /// exactly fill the seats, otherwise the manually chosen pilots who still qualify. The list is
    /// remembered on the craft, as the reference rewrites <c>_pilots</c>.
    /// </summary>
    private (IReadOnlyList<CampaignState.SoldierState> Pilots, IReadOnlyList<int> Ids) PilotList(CampaignState.BaseState owner,
        CampaignState.CraftState craft, Oxce.Mods.Rulesets.Runtime.RuntimeCraftRule rule, string type, CraftLogisticsState state)
    {
        if (rule.Pilots == 0) return ([], []);
        var rules = campaign.Content.RuntimeRules;
        bool OnCraft(CampaignState.SoldierState soldier) =>
            soldier.Personal is { } personal && personal.CraftType == type && personal.CraftId == craft.Id;
        bool Qualifies(CampaignState.SoldierState soldier) =>
            SoldierPiloting.MeetsRequirements(soldier.Personal!, rules.Soldiers[soldier.Rule].Value, rule, rules);
        var result = owner.Soldiers.Where(soldier => OnCraft(soldier) && Qualifies(soldier)).ToList();
        if (result.Count != rule.Pilots)
        {
            result.Clear();
            foreach (var id in state.Pilots)
            {
                var soldier = owner.Soldiers.FirstOrDefault(s => OnCraft(s) && s.Id == id && Qualifies(s));
                if (soldier is not null) result.Add(soldier);
                if (result.Count >= rule.Pilots) break;
            }
        }
        var ids = new List<int>();
        foreach (var soldier in result)
            if (!ids.Contains(soldier.Id)) ids.Add(soldier.Id);
        return (result, ids);
    }

    // Soldier::getStatsWithSoldierBonusesOnly: current statistics plus transformation and commendation bonuses.
    private DogfightPilot PilotStats(SoldierPersonalState personal)
    {
        var rules = campaign.Content.RuntimeRules;
        var bonuses = new HashSet<Oxce.Mods.Rulesets.Runtime.RuleHandle<Oxce.Mods.Rulesets.Runtime.SoldierBonusRuleFamily>>();
        foreach (var id in personal.TransformationBonuses.Keys)
            if (rules.SoldierBonuses.TryGet(id, out var handle)) bonuses.Add(handle);
        foreach (var commendation in personal.Commendations)
        {
            if (!rules.Commendations.TryGet(commendation.RuleId, out var commendationHandle)) continue;
            var types = rules.Commendations[commendationHandle].Value.SoldierBonusTypes;
            if (types.Count != 0 && rules.SoldierBonuses.TryGet(types[Math.Clamp(commendation.DecorationLevel, 0, types.Count - 1)], out var bonus))
                bonuses.Add(bonus);
        }
        int Stat(string key)
        {
            short value = personal.CurrentStats.GetValueOrDefault(key);
            foreach (var bonus in bonuses) value = unchecked((short)(value + rules.SoldierBonuses[bonus].Value.Stats.GetValueOrDefault(key)));
            return value;
        }
        return new DogfightPilot(Stat("firing"), Stat("reactions"), Stat("bravery"));
    }

    private int CraftMaximumAltitude(DogfightCraft craft)
    {
        var (_, state) = FindInterceptionCraft((InterceptionCraftKey)craft.Key);
        return campaign.Content.RuntimeRules.Crafts[state.Rule].Value.MaximumAltitude;
    }

    private (CampaignState.BaseState Owner, CampaignState.CraftState Craft) FindInterceptionCraft(InterceptionCraftKey key)
    {
        if (!TryFindCraft(key.BaseId, key.TypeId, key.Id, out var owner, out var index))
            throw new InvalidOperationException("An intercepting craft no longer exists.");
        return (owner, owner.Crafts[index]);
    }

    private bool InsideLand(DogfightUfo ufo) =>
        WorldGeometry.InsideLand(campaign.Content.RuntimeRules.Campaign.Globe, new WorldPosition(ufo.Longitude, ufo.Latitude));

    /// <summary>Copies a window's craft and UFO fields back into the saved world state.</summary>
    private void WriteBack(Dogfight dogfight)
    {
        var key = (InterceptionCraftKey)dogfight.Craft.Key;
        if (TryFindCraft(key.BaseId, key.TypeId, key.Id, out var owner, out var index) && owner.Crafts[index].Logistics is { } state)
        {
            var weapons = state.Weapons.ToArray();
            for (var slot = 0; slot < weapons.Length; slot++)
                if (weapons[slot] is { } saved && slot < dogfight.Craft.Weapons.Count && dogfight.Craft.Weapons[slot] is { } weapon)
                    weapons[slot] = saved with { Ammo = weapon.Ammo, Disabled = weapon.Disabled };
            owner.Crafts[index] = owner.Crafts[index] with
            {
                Logistics = state with
                {
                    Damage = dogfight.Craft.Damage,
                    Shield = dogfight.Craft.Shield,
                    InterceptionOrder = dogfight.Craft.InterceptionOrder,
                    Weapons = Array.AsReadOnly(weapons),
                },
            };
        }
        var ufo = dogfight.Ufo;
        var ufoIndex = UfoIndex((int)ufo.Key);
        if (ufoIndex < 0) return;
        var snapshot = _ufos[ufoIndex];
        var speedChanged = snapshot.Speed != ufo.Speed;
        _ufos[ufoIndex] = snapshot with
        {
            Damage = ufo.Damage,
            Shield = ufo.Shield,
            EscapeCountdown = ufo.EscapeCountdown,
            FireCountdown = ufo.FireCountdown,
            ShieldRechargeHandle = ufo.ShieldRechargeHandle,
            SoftlockShotCounter = ufo.SoftlockShotCounter,
            HunterKiller = ufo.HunterKiller,
            Status = ufo.Status switch
            {
                DogfightUfoStatus.Landed => UfoStatus.Landed,
                DogfightUfoStatus.Crashed => UfoStatus.Crashed,
                DogfightUfoStatus.Destroyed => UfoStatus.Destroyed,
                _ => snapshot.Status,
            },
            SecondsRemaining = ufo.SecondsRemaining,
            Altitude = ufo.Altitude,
            CrashId = ufo.CrashId,
            LandId = ufo.LandId,
            Speed = ufo.Speed,
            SpeedRadian = speedChanged ? WorldGeometry.RadianSpeed(ufo.Speed) : snapshot.SpeedRadian,
            SpeedLongitude = speedChanged ? 0 : snapshot.SpeedLongitude,
            SpeedLatitude = speedChanged ? 0 : snapshot.SpeedLatitude,
        };
    }
}
