namespace Oxce.Gameplay.Campaigns;

/// <summary>Needed-item return at Base::removeResearch, before corpse recovery.</summary>
public static class ResearchItemLifecycle
{
    public static bool ReturnsHeldItem(bool holding, bool returns, bool finished) => holding && (!finished || returns);
}
