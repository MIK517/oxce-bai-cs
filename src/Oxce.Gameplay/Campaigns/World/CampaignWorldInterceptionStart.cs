using Oxce.Gameplay.Campaigns.World.Interception;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>A craft and a UFO met: an interception window opened (GeoscapeState::startDogfight). It pauses time.</summary>
public sealed record InterceptionStarted(int Interception, int BaseId, string CraftTypeId, int CraftId, int UfoUniqueId,
    bool UfoAttacking) : ICampaignEvent;

/// <summary>DogfightErrorState: a water-only craft cannot engage yet; its window waits minimized.</summary>
public sealed record InterceptionWaiting(int Interception, string TextKey) : ICampaignEvent;

/// <summary>A waiting interception can engage now; time stops so the window can open.</summary>
public sealed record InterceptionReady(int Interception) : ICampaignEvent;

/// <summary>
/// Interception starts inside <c>GeoscapeState::time5Seconds</c>: a hunter-killer reaching its craft
/// (UFO pass), a craft reaching a flying UFO (craft pass), escorts joining a hunter-killer fight, the
/// four-window limit and hunter-killer exclusivity, the depth and land waits of water-only craft, the
/// destroyed-UFO cleanup and the end-of-tick wait checks. A window opened this tick waits like
/// <c>_dogfightsToBeStarted</c> and gets its slot when the tick's starts are processed, latest first.
/// </summary>
internal sealed partial class CampaignWorld
{
    private readonly List<Dogfight> _pendingInterceptions = [];
    private readonly Dictionary<Dogfight, string> _pendingInterceptionErrors = [];
    // GeoscapeState::time5Seconds's local ufoIsAttacking: a hunter-killer attack this tick stops the
    // rest of the craft pass from moving, recharging shields or arriving.
    private bool _ufoIsAttackingThisTick;

    private void RegisterInterceptionStart(CampaignCapabilityRegistry registry)
    {
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsWorldInterceptionStart,
            "interception start", StartPendingInterceptions);
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsWorldInterceptionCleanup,
            "destroyed UFO interception cleanup", RemoveDestroyedUfoInterceptions);
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsWorldInterceptionWaits,
            "interception waits", CheckInterceptionWaits);
    }

    private IEnumerable<Dogfight> OpenAndPendingInterceptions => _interceptions.Concat(_pendingInterceptions);

    /// <summary>
    /// The UFO pass: a hunting UFO reached the craft it hunts. Returns false when the reference
    /// condition fails (no live craft target), true when the attack was handled; the caller then skips
    /// the UFO's shield recharge as the reference <c>continue</c> does.
    /// </summary>
    private bool TryHunterKillerAttack(int ufoIndex)
    {
        var ufo = _ufos[ufoIndex];
        if (!ufo.Hunting || ufo.Destination is not { Kind: WorldTargetKind.Craft } target) return false;
        if (CraftKeyFor(target) is not { } key || !TryFindCraft(key.BaseId, key.TypeId, key.Id, out var owner, out var craftIndex) ||
            owner.Crafts[craftIndex].Logistics is not { } state ||
            CraftLogistics.IsDestroyed(state, campaign.Content.RuntimeRules.Crafts[owner.Crafts[craftIndex].Rule].Value, campaign.Content.RuntimeRules))
            return false;
        // Not more than one hunter-killer interception at a time: this one waits.
        if (OpenAndPendingInterceptions.Any(static d => d.UfoIsAttacking)) return true;
        ClearInterceptionsForHunterKiller();
        _pendingInterceptions.Add(NewInterception(owner, craftIndex, ufoIndex, ufoIsAttacking: true));
        JoinEscorts(owner, craftIndex, ufoIndex);
        _ufoIsAttackingThisTick = true;
        return true;
    }

    /// <summary>The craft pass: a craft reached a flying UFO.</summary>
    private void ArriveAtFlyingUfo(CampaignState.BaseState owner, int craftIndex, int ufoIndex)
    {
        var ufo = _ufos[ufoIndex];
        var craft = owner.Crafts[craftIndex];
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Crafts[craft.Rule].Value;
        var type = rules.Crafts.GetExternalId(craft.Rule);
        // Not more than four interceptions at a time, but hunter-killers are always allowed.
        if (!ufo.HunterKiller && OpenAndPendingInterceptions.Count() >= 4) return;
        if (CraftInDogfight(owner.Id, type, craft.Id)) return;
        if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, craft.Logistics!.Weapons, rules, out var speed) || ufo.Speed > speed) return;
        if (ufo.HunterKiller)
        {
            if (OpenAndPendingInterceptions.Any(static d => d.UfoIsAttacking)) return;
            ClearInterceptionsForHunterKiller();
            _ufoIsAttackingThisTick = true;
        }
        var window = NewInterception(owner, craftIndex, ufoIndex, ufo.HunterKiller);
        _pendingInterceptions.Add(window);
        if (ufo.HunterKiller)
        {
            JoinEscorts(owner, craftIndex, ufoIndex);
            return;
        }
        // Hunter-killers ignore these restrictions; otherwise they would be easy to avoid.
        var state = owner.Crafts[craftIndex].Logistics!;
        if (rule.IsWaterOnly && WorldAltitudes.Index(ufo.Altitude) > rule.MaximumAltitude)
        {
            _pendingInterceptionErrors[window] = "STR_UNABLE_TO_ENGAGE_DEPTH";
            window.Minimized = true;
            window.WaitForAltitude = true;
        }
        else if (rule.IsWaterOnly && !WorldGeometry.InsideLand(rules.Campaign.Globe, new WorldPosition(state.Longitude, state.Latitude)))
        {
            _pendingInterceptionErrors[window] = "STR_UNABLE_TO_ENGAGE_AIRBORNE";
            window.Minimized = true;
            window.WaitForPoly = true;
        }
    }

    // A hunter-killer interrupts every other interception to prevent a dead-lock: their crafts leave the
    // fight without the windows' own end handling.
    private void ClearInterceptionsForHunterKiller()
    {
        foreach (var dogfight in OpenAndPendingInterceptions.ToArray())
        {
            dogfight.Craft.InDogfight = false;
            dogfight.Craft.InterceptionOrder = 0;
            WriteBack(dogfight);
        }
        _interceptions.Clear();
        _pendingInterceptions.Clear();
        _pendingInterceptionErrors.Clear();
    }

    // Escorts within escortRange with a loaded weapon join a hunter-killer fight, up to three, ahead of
    // the main target (push_front), when escortsJoinFightAgainstHK is on.
    private void JoinEscorts(CampaignState.BaseState mainOwner, int mainIndex, int ufoIndex)
    {
        var rules = campaign.Content.RuntimeRules;
        if (!rules.Campaign.Interception.EscortsJoinFightAgainstHK) return;
        var main = mainOwner.Crafts[mainIndex].Logistics!;
        var mainPosition = new WorldPosition(main.Longitude, main.Latitude);
        var range = WorldGeometry.Nautical(rules.Campaign.EscortRange);
        var secondary = 0;
        foreach (var owner in campaign.BaseStates)
        {
            for (var index = 0; index < owner.Crafts.Count; index++)
            {
                if (ReferenceEquals(owner, mainOwner) && index == mainIndex) continue;
                var craft = owner.Crafts[index];
                if (craft.Logistics is not { Status: "STR_OUT" } state ||
                    CraftLogistics.IsDestroyed(state, rules.Crafts[craft.Rule].Value, rules)) continue;
                // Craft::isIgnoredByHK: returning from a mission or low on fuel.
                if (state.MissionComplete || state.LowFuel) continue;
                if (rules.Crafts[craft.Rule].Value.WeaponSlots == 0 || !state.Weapons.Any(static w => w is { Ammo: > 0 })) continue;
                if (!(WorldGeometry.Distance(new WorldPosition(state.Longitude, state.Latitude), mainPosition) < range)) continue;
                if (secondary >= 3) continue;
                _pendingInterceptions.Insert(0, NewInterception(owner, index, ufoIndex, ufoIsAttacking: true));
                secondary++;
            }
        }
    }

    private Dogfight NewInterception(CampaignState.BaseState owner, int craftIndex, int ufoIndex, bool ufoIsAttacking)
    {
        var world = new InterceptionWorld(this, forecast: false, [.. OpenAndPendingInterceptions]);
        // The window's slot is 0 until it opens, as in the reference constructor.
        var dogfight = new Dogfight(world, InterceptionCraft(owner, craftIndex), InterceptionUfo(_ufos[ufoIndex]), ufoIsAttacking, 0);
        WriteBack(dogfight);
        return dogfight;
    }

    private void StartPendingInterceptions(CampaignState.TimeEffects effects)
    {
        if (_pendingInterceptions.Count == 0) return;
        while (_pendingInterceptions.Count != 0)
        {
            var dogfight = _pendingInterceptions[^1];
            _pendingInterceptions.RemoveAt(_pendingInterceptions.Count - 1);
            dogfight.AssignNumber(FirstFreeInterceptionSlot());
            _interceptions.Add(dogfight);
            var key = (InterceptionCraftKey)dogfight.Craft.Key;
            effects.Notify(new InterceptionStarted(dogfight.InterceptionNumber, key.BaseId, key.TypeId, key.Id, (int)dogfight.Ufo.Key,
                dogfight.UfoIsAttacking));
            if (_pendingInterceptionErrors.Remove(dogfight, out var error))
                effects.Notify(new InterceptionWaiting(dogfight.InterceptionNumber, error));
        }
    }

    // GeoscapeState::time5Seconds deletes the windows of destroyed UFOs with the UFOs themselves.
    private void RemoveDestroyedUfoInterceptions(CampaignState.TimeEffects _)
    {
        if (_ufoArrivalEndedTick || _interceptions.Count == 0) return;
        _interceptions.RemoveAll(dogfight => UfoIndex((int)dogfight.Ufo.Key) is var index && index >= 0 &&
            _ufos[index].Status == UfoStatus.Destroyed);
    }

    // The end of time5Seconds: a waiting window whose UFO came within reach stops the clock.
    private void CheckInterceptionWaits(CampaignState.TimeEffects effects)
    {
        if (_ufoArrivalEndedTick) return;
        foreach (var dogfight in _interceptions)
        {
            if (!dogfight.Minimized || UfoIndex((int)dogfight.Ufo.Key) is not (var index and >= 0)) continue;
            var ufo = _ufos[index];
            if (dogfight.WaitForPoly && WorldGeometry.InsideLand(campaign.Content.RuntimeRules.Campaign.Globe, ufo.Position) ||
                dogfight.WaitForAltitude && WorldAltitudes.Index(ufo.Altitude) <= CraftMaximumAltitude(dogfight.Craft))
                effects.Notify(new InterceptionReady(dogfight.InterceptionNumber));
        }
    }
}
