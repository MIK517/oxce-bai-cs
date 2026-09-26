using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>GeoscapeState::time10Minutes/DetectXCOMBase; retaliation mission creation is separate.</summary>
internal sealed partial class CampaignWorld
{
    private string? UfoBaseDetectionReason(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.TenMinutes) return null;
        var hasScanner = false;
        // Validate every possible scanner before any handler consumes RNG or mutates state.
        foreach (var ufo in _ufos)
            if (UfoCanScanBases(ufo, out _)) hasScanner = true;
        if (!hasScanner) return null;
        for (var index = 0; index < campaign.BaseStates.Count; index++)
        {
            if (!TryBaseDetectionChance(campaign.BaseStates[index], highest, out _))
                return "Base detection inputs are outside the supported range.";
        }
        return null;
    }

    private void DetectXcomBases(CampaignState.TimeEffects _)
    {
        if (_ufos.Count == 0) return;
        // Allocate regional selection storage only after a successful non-aggressive
        // scan. Index 0 represents locateRegion's null result (bases outside all regions).
        CampaignState.BaseState?[]? discovered = null;
        for (var index = 0; index < campaign.BaseStates.Count; index++)
        {
            var owner = campaign.BaseStates[index];
            int? chance = null;
            foreach (var ufo in _ufos)
            {
                if (!UfoCanScanBases(ufo, out var sightRange) ||
                    WorldGeometry.Distance(new WorldPosition(owner.Longitude, owner.Latitude), ufo.Position) >=
                    WorldGeometry.Nautical(sightRange)) continue;
                if (chance is null)
                {
                    if (!TryBaseDetectionChance(owner, CampaignTimeTrigger.FiveSeconds, out var currentChance))
                        throw new InvalidOperationException("Base detection changed after world preflight.");
                    chance = currentChance;
                }
                if (!Percent(chance.Value)) continue;
                if (campaign.Options.AggressiveRetaliation) owner.RetaliationTarget = true;
                else
                {
                    discovered ??= new CampaignState.BaseState?[campaign.RegionStates.Count + 1];
                    discovered[LocateBaseRegion(owner) + 1] = owner;
                }
                // std::find_if stops at the first successful UFO for each base,
                // even if this base was already marked on an earlier scan.
                break;
            }
        }
        if (discovered is not null)
            foreach (var owner in discovered)
                if (owner is not null) owner.RetaliationTarget = true;
    }

    private int LocateBaseRegion(CampaignState.BaseState owner)
    {
        var position = new WorldPosition(owner.Longitude, owner.Latitude);
        for (var index = 0; index < campaign.RegionStates.Count; index++)
            if (WorldGeometry.InsideRegion(campaign.Content.RuntimeRules.Regions[
                campaign.RegionStates[index].Rule].Value, position)) return index;
        return -1;
    }

    private bool UfoCanScanBases(UfoSnapshot ufo, out int sightRange)
    {
        sightRange = 0;
        if (ufo.TrajectoryPoint <= 1) return false;
        var rules = campaign.Content.RuntimeRules;
        var trajectory = rules.UfoTrajectories[rules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
        if (trajectory.Zone(ufo.TrajectoryPoint) == 5) return false;
        var mission = FindMission(ufo.MissionId) ??
            throw new InvalidDataException("UFO base detection requires a valid mission link.");
        var missionRule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
        if (!campaign.Options.AggressiveRetaliation && missionRule.Objective != RuntimeMissionObjective.Retaliation ||
            ufo.TrajectoryId == RuntimeUfoTrajectoryRule.RetaliationAssaultRun || ufo.Status == UfoStatus.IgnoreMe)
            return false;
        var rule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        var bonus = rule.RaceBonus(mission.Race);
        var damageMaximum = checked(rule.Stats.DamageMaximum + bonus.DamageMaximum);
        // isCrashed tests damage, not saved status: a healthy terminal-departure UFO
        // can still detect a base before the five-second cleanup removes it.
        if (ufo.Damage >= damageMaximum ||
            !rule.Unmanned && ufo.HuntBehavior != 1 && ufo.Damage > damageMaximum / 2) return false;
        sightRange = checked(rule.Stats.SightRange + bonus.SightRange);
        return sightRange > 0;
    }

    private bool TryBaseDetectionChance(CampaignState.BaseState owner, CampaignTimeTrigger highest, out int chance)
    {
        chance = 0;
        long size = 0;
        long shields = 0;
        foreach (var facility in owner.Facilities)
        {
            // Daily construction finishes before this ten-minute handler.
            if (facility.BuildTime != 0 &&
                !(highest >= CampaignTimeTrigger.OneDay && facility.BuildTime == 1)) continue;
            var rule = campaign.Content.RuntimeRules.Facilities[facility.Rule].Value;
            if (rule.SizeX < 0 || rule.SizeY < 0) return false;
            size = checked(size + (long)rule.SizeX * rule.SizeY);
            if (rule.MindShield && !facility.Disabled)
            {
                if (rule.MindShieldPower < 0) return false;
                shields = checked(shields + rule.MindShieldPower);
            }
        }
        chance = WorldDetection.BaseDetectionChance(size, shields);
        return true;
    }
}
