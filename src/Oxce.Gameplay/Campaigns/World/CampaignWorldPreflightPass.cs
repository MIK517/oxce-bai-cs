namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private readonly WorldPreflightPass _pass = new();

    private IReadOnlySet<string> GuaranteedDailyResearch() =>
        _pass.GuaranteedDailyResearch ??= campaign.ResearchPreflight.GuaranteedResearchAfterDailyProgress();

    /// <summary>
    /// Scratch state of one world preflight pass: pure forecasts, proofs and work counters read by
    /// later checks in the same pass and by the craft-movement preflight. None of it is persisted,
    /// and time handlers do not depend on it. A new forecast belongs here, with its reset in the
    /// matching <c>Begin</c> method, so no pass can observe a value left over from an earlier tick.
    /// Reusable buffers retain their capacity across passes; forecasts keep their existing lazy
    /// allocations.
    /// </summary>
    private sealed class WorldPreflightPass
    {
        // Reset by BeginWorld, when the world preflight starts.

        // GuaranteedDailyResearch when a daily mission interruption or alien base self-destruction
        // depends on it, otherwise null; missions are projected through it after daily progress.
        public IReadOnlySet<string>? DailyResearchForecast;
        public Dictionary<int, AlienMissionSnapshot>? MissionCounterForecast;
        public List<(AlienMissionSnapshot Mission, bool Hunting)>? GeneratedWaveMissions;
        public HashSet<(int Source, string Deployment, string Mission)>? DailyGeneratedWaveProofs;
        public HashSet<(int Source, string Deployment, string Mission)>? HuntingGeneratedWaveProofs;
        public bool CollectHuntingGeneratedWaves;
        public int NewFlyingHunterBirths;
        public long HuntingRetargetWorkBound;
        public long EscortMissionLookupActors;
        public bool? NewHunterHasNoTarget;
        public CampaignTimeTrigger NewbornHighestTrigger;
        public List<CustomCounterChange>? SiteBirthIdChanges;
        public int ObjectiveBaseBirths;
        public int ObjectiveBasesBeforeHunting;
        public int ObjectiveBaseHuntingMissions;
        public List<(IReadOnlyList<SpawnArea> Positions, int Points)>? ObjectiveBaseScores;
        public List<CustomCounterChange>? SiteBirthExpiryIncreases;
        public List<CustomCounterChange>? SiteBirthExpiryDecreases;
        public List<CustomCounterForecast>? SiteConversionCounterForecast;
        public List<CustomCounterForecast>? BeforeHuntingSiteCounterForecast;
        public HashSet<string>? EarlyLiveWorldMarkers;
        // Research known to be complete after this tick's daily progress. One pure projection per
        // pass, shared by every daily check of both world and craft-movement preflights.
        public IReadOnlySet<string>? GuaranteedDailyResearch;
        public HashSet<int>? SuppressedUfoSites;
        // Target::getDistance from a UFO to its destination, memoized by the exact bits of both
        // coordinates. Distance is a pure function of those four values, so an entry can never be
        // stale; re-projected UFOs (retarget, suppression, escort target updates) simply produce other
        // keys. Keys hold no references, so per-tick projections are not kept alive across a GC. The
        // world preflight clears the table when it starts, bounding it to one pass; the craft-movement
        // preflight later in the same pass reuses the entries. Only distances are cached, never an
        // outcome that depends on pass-local forecasts.
        public readonly Dictionary<(long, long, long, long), double> UfoDestinationDistances = [];

        // Reset by BeginHunting, when hunting and escort forecasts start later in the pass. The
        // checks that run before it never read these values.
        public Dictionary<int, UfoSnapshot>? RetargetForecast;
        public readonly List<WorldHuntCraft> HuntingCandidates = [];
        public readonly Dictionary<CampaignState.CraftState, HuntingCraftFacts> HuntingFacts =
            new(ReferenceEqualityComparer.Instance);
        public Dictionary<int, UfoSnapshot>? NewLeaderFlightForecast;
        public int PossibleHuntMarkerIds;
        public long HuntingForecastWork;
        public Dictionary<(string TypeId, int Id), CraftLogisticsState>? HuntingCraftForecast;
        public Dictionary<int, List<CampaignState.CraftState>>? HuntingCraftSources;
        public long? BaseHuntingCraftPopulationBound;
        public IReadOnlySet<string>? BaseHuntingCraftRemovalTypes;
        public readonly HashSet<NewbornHuntingProof> NewbornNoTargetProofs = [];
        public readonly HashSet<NewbornHuntingProof> NewbornSafeHuntingProofs = [];
        public List<WorldPosition>? EscortFlightPositions;
        public (UfoSnapshot? Source, UfoSnapshot? Projected)[]? EscortFlightTargets;
        public int EscortFlightEndIndex;

        /// <summary>Starts a world preflight pass.</summary>
        public void BeginWorld(CampaignTimeTrigger highest)
        {
            UfoDestinationDistances.Clear();
            DailyResearchForecast = null;
            MissionCounterForecast = null;
            GeneratedWaveMissions = null;
            DailyGeneratedWaveProofs = null;
            HuntingGeneratedWaveProofs = null;
            CollectHuntingGeneratedWaves = false;
            NewFlyingHunterBirths = 0;
            HuntingRetargetWorkBound = 0;
            EscortMissionLookupActors = 0;
            NewHunterHasNoTarget = null;
            NewbornHighestTrigger = highest;
            SiteBirthIdChanges = null;
            ObjectiveBaseBirths = 0;
            ObjectiveBasesBeforeHunting = 0;
            ObjectiveBaseHuntingMissions = 0;
            ObjectiveBaseScores = null;
            SiteBirthExpiryIncreases = null;
            SiteBirthExpiryDecreases = null;
            SiteConversionCounterForecast = null;
            BeforeHuntingSiteCounterForecast = null;
            EarlyLiveWorldMarkers = null;
            GuaranteedDailyResearch = null;
            SuppressedUfoSites = null;
        }

        /// <summary>Starts the hunting and escort forecasts of the current pass.</summary>
        public void BeginHunting()
        {
            RetargetForecast = null;
            HuntingCandidates.Clear();
            HuntingFacts.Clear();
            NewLeaderFlightForecast = null;
            PossibleHuntMarkerIds = 0;
            HuntingForecastWork = 0;
            HuntingCraftForecast = null;
            HuntingCraftSources = null;
            BaseHuntingCraftPopulationBound = null;
            BaseHuntingCraftRemovalTypes = null;
            NewbornNoTargetProofs.Clear();
            NewbornSafeHuntingProofs.Clear();
            ResetEscortFlight();
        }

        /// <summary>
        /// Invalidates the ordered escort flight cache. Also called inside a pass when a forecast
        /// changes an earlier actor's flight outcome (a suppressed site conversion).
        /// </summary>
        public void ResetEscortFlight()
        {
            EscortFlightPositions?.Clear();
            if (EscortFlightTargets is { } cached) Array.Clear(cached);
            EscortFlightEndIndex = int.MaxValue;
        }
    }
}
