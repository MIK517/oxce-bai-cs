namespace Oxce.Gameplay.Campaigns;

/// <summary>Needed-item return at Base::removeResearch, before corpse recovery.</summary>
public static class ResearchItemLifecycle
{
    public static bool ReturnsHeldItem(bool holding, bool returns, bool finished) => holding && (!finished || returns);

    public static int CorpseQuantity(bool retain, bool needsItem, bool destroysItem, bool returnsItem,
        bool finished, bool dailyCompletion, bool recoverable) =>
        retain && needsItem && destroysItem && finished && recoverable
            ? (returnsItem ? 0 : 1) + (dailyCompletion ? 1 : 0) : 0;
}
