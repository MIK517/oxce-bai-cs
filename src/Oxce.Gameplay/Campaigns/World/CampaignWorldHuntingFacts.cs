using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    // Craft state is immutable, but the owning craft may be replaced or serviced between
    // callers. Preflight and each live retarget invocation therefore use distinct caches.
    private readonly Dictionary<CampaignState.CraftState, HuntingCraftFacts> _huntingFacts =
        new(ReferenceEqualityComparer.Instance);

    private HuntingCraftFacts HuntingFactsFor(CampaignState.CraftState craft, CraftLogisticsState state,
        RuntimeCraftRule rule, Dictionary<CampaignState.CraftState, HuntingCraftFacts> cache)
    {
        if (cache.TryGetValue(craft, out var facts) && ReferenceEquals(facts.Source, state)) return facts;
        facts = new(state, CraftLogistics.EffectiveDamageMaximum(state, rule, campaign.Content.RuntimeRules));
        cache[craft] = facts;
        return facts;
    }

    private bool ReadHuntingSpeed(ref HuntingCraftFacts facts, RuntimeCraftRule rule)
    {
        if (!facts.SpeedRead)
        {
            facts.SpeedSupported = CraftLogistics.TryEffectiveSpeedMaximum(rule, facts.Source.Weapons,
                campaign.Content.RuntimeRules, out facts.Speed);
            facts.SpeedRead = true;
        }
        return facts.SpeedSupported;
    }

    private bool ReadHuntingCapacity(ref HuntingCraftFacts facts, RuntimeCraftRule rule)
    {
        if (!facts.CapacityRead)
        {
            facts.CapacitySupported = CraftLogistics.TryEffectiveUnitCapacities(rule, facts.Source.Weapons,
                campaign.Content.RuntimeRules, out var capacity);
            if (facts.CapacitySupported) facts.Units = capacity.SoldierCapacity;
            facts.CapacityRead = true;
        }
        return facts.CapacitySupported;
    }

    private struct HuntingCraftFacts(CraftLogisticsState source, long damageMaximum)
    {
        public CraftLogisticsState Source = source;
        public long DamageMaximum = damageMaximum;
        public bool SpeedRead;
        public bool SpeedSupported;
        public int Speed;
        public bool CapacityRead;
        public bool CapacitySupported;
        public int Units;
        public WorldTargetReference? Reference;
    }
}
