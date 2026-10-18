using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>determineAlienMissions' final monthly phase, after arc, mission and event scheduling.</summary>
internal interface ICampaignWorldAlienBaseMonthly
{
    CampaignCommandResult Advance(bool isNewMonth);
}

/// <summary>Isolated subsystem seam; the month rollover runs the same phase through world scheduling.</summary>
internal sealed record AdvanceMonthlyWorldAlienBases(bool IsNewMonth = true) : ICampaignCommand;

/// <summary>One possible operation-base deployment/race before monthly upgrades.</summary>
internal readonly record struct MonthlyBaseBirthChoice(string DeploymentId, string Race);

internal sealed partial class CampaignWorld : ICampaignWorldAlienBaseMonthly
{
    private const string BaseEvolutionLoopReason = "Alien race evolution reaches the reference 100-transition limit.";
    private const string BaseMonthlyWorkReason = "Alien base monthly processing exceeds the supported work limit.";
    private const string BaseUpgradeIdentityReason = "Alien base upgrades can collide with another world target's marker identity.";

    private void RegisterAlienBaseMonthly(CampaignCapabilityRegistry registry)
    {
        registry.Operation<ICampaignWorldAlienBaseMonthly>(this);
        registry.Command<AdvanceMonthlyWorldAlienBases>(command =>
            ((ICampaignWorldAlienBaseMonthly)this).Advance(command.IsNewMonth));
        registry.Timed(CampaignTimeTrigger.OneMonth, CampaignTimeOrder.MonthWorldAlienBaseDetection, "monthly alien base detection",
            DetectAlienBaseMonthly);
    }

    /// <summary>
    /// GeoscapeState::time1Month, after the report: X-COM operatives find the first undiscovered alien base with
    /// <c>chanceToDetectAlienBaseEachMonth</c>. The roll is made only while an alien base exists.
    /// </summary>
    private void DetectAlienBaseMonthly(CampaignState.TimeEffects effects)
    {
        if (_alienBases.Count == 0 || !Percent(campaign.Content.RuntimeRules.Campaign.Monthly.ChanceToDetectAlienBaseEachMonth)) return;
        var index = _alienBases.FindIndex(static alienBase => !alienBase.Discovered);
        if (index < 0) return;
        var discovered = _alienBases[index] with { Discovered = true };
        _alienBases[index] = discovered;
        effects.Notify(new AlienBaseDiscovered(discovered.Id, discovered.DeploymentId));
    }

    CampaignCommandResult ICampaignWorldAlienBaseMonthly.Advance(bool isNewMonth)
    {
        var month = campaign.MonthsPassed;
        if (!isNewMonth || month <= 0) return new([]);
        if (AlienBaseMonthlyReason(month) is { } reason) return Blocked(reason);
        return RunAlienBaseMonthly(month);
    }

    private CampaignCommandResult RunAlienBaseMonthly(int month)
    {
        var rules = campaign.Content.RuntimeRules;
        // The reference upgrades ALL bases before running evolution on any base.
        for (var index = 0; index < _alienBases.Count; index++)
        {
            var current = _alienBases[index];
            var source = AlienBaseRule(current);
            var weights = UpgradeWeights(source, month, current.StartMonth);
            if (weights is null) continue;
            var choice = WorldAlienMissionGeneration.Choose(weights, campaign.Random);
            if (!rules.AlienDeployments.TryGet(choice, out var handle) || choice == current.DeploymentId) continue;
            var updated = UpgradeBase(current, source, choice, rules.AlienDeployments[handle].Value, month);
            _alienBases[index] = updated;
            RepairUpgradedBaseReferences(current, updated);
        }
        long work = 0;
        for (var index = 0; index < _alienBases.Count; index++)
        {
            var current = _alienBases[index];
            if (EvolvedRace(AlienBaseRule(current), current.Race, month, ref work, out var race) is { } changedReason)
                throw new InvalidOperationException(changedReason);
            if (race != current.Race) _alienBases[index] = current with { Race = race };
        }
        return new([]);
    }

    private string? AlienBaseMonthlyReason(int month,
        IReadOnlyDictionary<string, long>? prospectiveMarkers = null, int additionalMissions = 0,
        IReadOnlyCollection<MonthlyBaseBirthChoice>? prospectiveBases = null)
    {
        if (_alienBases.Count > 100_000 || _sites.Count > 100_000) return BaseMonthlyWorkReason;
        var identities = new Dictionary<(string Marker, int Id), int>();
        long work = _alienBases.Count + _sites.Count;
        long craftReferences = 0;
        foreach (var owner in campaign.BaseStates)
        {
            craftReferences += owner.Crafts.Count;
            if (work + craftReferences > MaximumWorldWork) return BaseMonthlyWorkReason;
            foreach (var craft in owner.Crafts)
                identities.TryAdd((campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id), -1);
        }
        work += craftReferences;
        foreach (var site in _sites) identities.TryAdd((MarkerName(site.DeploymentId), site.Id), -1);
        var newbornOwners = new Dictionary<string, int>(StringComparer.Ordinal);
        if (prospectiveBases is not null && prospectiveMarkers is not null)
            foreach (var birth in prospectiveBases)
            {
                if (++work > MaximumWorldWork) return BaseMonthlyWorkReason;
                var marker = MarkerName(birth.DeploymentId);
                if (newbornOwners.ContainsKey(marker)) continue;
                var owner = -2 - newbornOwners.Count;
                newbornOwners.Add(marker, owner);
                for (long id = campaign.PeekNextId(marker); id <= prospectiveMarkers.GetValueOrDefault(marker); id++)
                {
                    if (++work > MaximumWorldWork) return BaseMonthlyWorkReason;
                    if (!identities.TryAdd((marker, checked((int)id)), owner)) return BaseUpgradeIdentityReason;
                }
            }
        for (var index = 0; index < _alienBases.Count; index++)
            if (Check(_alienBases[index], index) is { } reason) return reason;
        if (prospectiveBases is not null && prospectiveMarkers is not null)
            foreach (var birth in prospectiveBases)
            {
                var marker = MarkerName(birth.DeploymentId);
                for (long id = campaign.PeekNextId(marker); id <= prospectiveMarkers.GetValueOrDefault(marker); id++)
                {
                    var current = new AlienBaseSnapshot(checked((int)id), birth.DeploymentId, birth.Race, 0, 0)
                    { StartMonth = month };
                    if (Check(current, newbornOwners[marker]) is { } reason) return reason;
                }
            }
        return null;

        string? Check(AlienBaseSnapshot current, int owner)
        {
            if (++work > MaximumWorldWork) return BaseMonthlyWorkReason;
            var source = AlienBaseRule(current);
            if (!ReserveIdentity(source.MarkerName, current.Id, owner)) return BaseUpgradeIdentityReason;
            work += source.AlienBaseUpgrades.Count;
            if (work > MaximumWorldWork) return BaseMonthlyWorkReason;
            IReadOnlyDictionary<string, ulong>? weights;
            try
            {
                weights = UpgradeWeights(source, month, current.StartMonth);
                work += weights?.Count ?? 0;
                if (work > MaximumWorldWork) return BaseMonthlyWorkReason;
                if (weights is not null) _ = WorldAlienMissionGeneration.Total(weights);
            }
            catch (InvalidDataException error) { return error.Message; }
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            if (weights is null || WorldAlienMissionGeneration.Total(weights) == 0) candidates.Add(current.DeploymentId);
            else
                foreach (var choice in weights)
                {
                    if (choice.Value == 0) continue;
                    candidates.Add(campaign.Content.RuntimeRules.AlienDeployments.TryGet(choice.Key, out _)
                        ? choice.Key : current.DeploymentId);
                }
            if (candidates.Any(id => campaign.Content.RuntimeRules.AlienDeployments[
                    campaign.Content.RuntimeRules.AlienDeployments.GetRequired(id)].Value.MarkerName != source.MarkerName))
            {
                work += _missions.Count + (long)additionalMissions + _ufos.Count + craftReferences;
                if (work > MaximumWorldWork) return BaseMonthlyWorkReason;
            }
            foreach (var id in candidates)
            {
                var destination = campaign.Content.RuntimeRules.AlienDeployments[
                    campaign.Content.RuntimeRules.AlienDeployments.GetRequired(id)].Value;
                if (!ReserveIdentity(destination.MarkerName, current.Id, owner)) return BaseUpgradeIdentityReason;
                if (id != current.DeploymentId && destination.HuntMissionMaxFrequency < 0)
                    return "Alien base upgrade hunting timer cannot be negative.";
                var projected = id == current.DeploymentId ? current : UpgradeBase(current, source, id, destination, month);
                if (EvolvedRace(destination, projected.Race, month, ref work, out _) is { } reason) return reason;
            }
            return null;
        }

        bool ReserveIdentity(string marker, int id, int owner)
        {
            // Craft::load remaps the legacy terror name before resolving site/base markers.
            if (marker is WorldTargetReference.BaseType or WorldTargetReference.UfoType or
                WorldTargetReference.WaypointType or "STR_ALIEN_TERROR")
                return false;
            var identity = (marker, id);
            if (identities.TryGetValue(identity, out var prior)) return prior == owner;
            identities.Add(identity, owner);
            return true;
        }
    }

    private static IReadOnlyDictionary<string, ulong>? UpgradeWeights(RuntimeAlienDeploymentRule deployment,
        int month, int startMonth)
    {
        if (deployment.AlienBaseUpgrades.Count == 0) return null;
        // C++ subtracts signed ints, then passes the result as size_t (including future start months).
        var age = unchecked((ulong)(month - startMonth));
        for (var index = deployment.AlienBaseUpgrades.Count - 1; index >= 0; index--)
            if (age >= deployment.AlienBaseUpgrades[index].FirstMonth) return deployment.AlienBaseUpgrades[index].Weights;
        throw new InvalidDataException("Alien base upgrade timeline has no entry for its current age.");
    }

    private AlienBaseSnapshot UpgradeBase(AlienBaseSnapshot current, RuntimeAlienDeploymentRule source,
        string id, RuntimeAlienDeploymentRule destination, int month) => current with
        {
            DeploymentId = id,
            StartMonth = source.ResetAlienBaseAgeAfterUpgrade || destination.ResetAlienBaseAge ? month : current.StartMonth,
            MinutesSinceLastHuntMissionGeneration = destination.HuntMissionMaxFrequency,
            Race = campaign.Content.RuntimeRules.AlienRaces.TryGet(destination.UpgradeRace, out _)
            ? destination.UpgradeRace : current.Race,
        };

    private string? EvolvedRace(RuntimeAlienDeploymentRule deployment, string initial, int month,
        ref long work, out string race)
    {
        race = initial;
        for (var transitions = 0; ;)
        {
            var changed = false;
            foreach (var step in deployment.AlienRaceEvolution)
            {
                if (++work > MaximumWorldWork) return BaseMonthlyWorkReason;
                if (step.Month > month || step.FromRace != race ||
                    !campaign.Content.RuntimeRules.AlienRaces.TryGet(step.ToRace, out _)) continue;
                race = step.ToRace;
                changed = true;
                break;
            }
            if (!changed) return null;
            if (++transitions >= 100) return BaseEvolutionLoopReason;
        }
    }

    private void RepairUpgradedBaseReferences(AlienBaseSnapshot oldBase, AlienBaseSnapshot newBase)
    {
        var oldMarker = MarkerName(oldBase.DeploymentId);
        var newMarker = MarkerName(newBase.DeploymentId);
        if (oldMarker == newMarker) return;
        WorldTargetReference? Repair(WorldTargetReference? target) =>
            target is { Kind: WorldTargetKind.AlienBase } && target.Id == oldBase.Id && target.TypeId == oldMarker
                ? target with { TypeId = newMarker, Longitude = newBase.Longitude, Latitude = newBase.Latitude } : target;
        for (var index = 0; index < _missions.Count; index++)
            if (_missions[index].AlienBase is { } link && Repair(link) is { } repaired && repaired != link)
                _missions[index] = _missions[index] with { AlienBase = repaired };
        for (var index = 0; index < _ufos.Count; index++)
            if (_ufos[index].Destination is { } target && Repair(target) is { } repaired && repaired != target)
                _ufos[index] = _ufos[index] with { Destination = repaired };
        foreach (var owner in campaign.BaseStates)
            for (var index = 0; index < owner.Crafts.Count; index++)
                if (owner.Crafts[index].Logistics is { Destination: { } target } state &&
                    Repair(target) is { } repaired && repaired != target)
                    owner.Crafts[index] = owner.Crafts[index] with { Logistics = state with { Destination = repaired } };
    }
}
