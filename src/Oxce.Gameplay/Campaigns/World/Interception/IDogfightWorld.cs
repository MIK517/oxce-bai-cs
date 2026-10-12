using Oxce.Core.Random;
using Oxce.Mods.Rulesets.CampaignStart;

namespace Oxce.Gameplay.Campaigns.World.Interception;

/// <summary>
/// What a dogfight step reads from and changes in the rest of the campaign. Calls happen exactly
/// where <c>DogfightState</c> makes them, so randomness drawn by the world stays in reference order.
/// </summary>
internal interface IDogfightWorld
{
    IRandomSource Random { get; }

    CampaignInterceptionSettings Settings { get; }

    /// <summary>SavedGame::getDifficulty.</summary>
    int Difficulty { get; }

    /// <summary>SavedGame::getMonthsPassed.</summary>
    int MonthsPassed { get; }

    /// <summary>Options::dogfightAI.</summary>
    bool DogfightAI { get; }

    /// <summary>Whether the craft's destination is still this UFO.</summary>
    bool CraftTargetsUfo(DogfightCraft craft, DogfightUfo ufo);

    /// <summary>GeoscapeState::handleDogfightExperience, after any UFO damage.</summary>
    void DogfightExperience();

    /// <summary>Globe::insideLand at the UFO.</summary>
    bool InsideLand(DogfightUfo ufo);

    /// <summary>Globe::insideFakeUnderwaterTexture at the UFO.</summary>
    bool InsideFakeUnderwater(DogfightUfo ufo);

    /// <summary>Ufo::move when a breaking-off UFO's fight ends.</summary>
    void MoveUfo(DogfightUfo ufo);

    /// <summary>Craft::setDestination(ufo): a pursuing craft keeps chasing a UFO that broke off.</summary>
    void CraftPursueUfo(DogfightCraft craft, DogfightUfo ufo);

    /// <summary>Craft::returnToBase.</summary>
    void CraftReturnToBase(DogfightCraft craft);

    /// <summary>Craft::think (one movement step); true when the craft reached its base.</summary>
    bool CraftThink(DogfightCraft craft);

    /// <summary>SavedGame::stopHuntingXcomCraft.</summary>
    void StopHuntingCraft(DogfightCraft craft);

    /// <summary>Crafts following a crashed UFO without units or landing ability return to base.</summary>
    void ReturnUnlandableFollowers(DogfightUfo ufo);

    /// <summary>Ufo::resetOriginalDestination(craft): a hunter-killer gives up its target.</summary>
    void ResetHunterDestination(DogfightUfo ufo, DogfightCraft craft);

    /// <summary>Ufo::setDestination(0) after a crash.</summary>
    void ClearUfoDestination(DogfightUfo ufo);

    /// <summary>AlienMission::ufoShotDown.</summary>
    void UfoShotDown(DogfightUfo ufo);

    /// <summary>RuleAlienMission::getRetaliationOdds of the UFO's mission (-1: the difficulty formula).</summary>
    int MissionRetaliationOdds(DogfightUfo ufo);

    /// <summary>AlienRace::getRetaliationAggression of the UFO's race.</summary>
    int RetaliationAggression(DogfightUfo ufo);

    /// <summary>The UFO mission's region.</summary>
    string UfoMissionRegion(DogfightUfo ufo);

    /// <summary>The region containing the craft's base (SavedGame::locateRegion).</summary>
    string CraftBaseRegion(DogfightCraft craft);

    /// <summary>SavedGame::findAlienMission(region, OBJECTIVE_RETALIATION, race).</summary>
    bool RetaliationMissionExists(string region, DogfightUfo ufo);

    /// <summary>Chooses and starts a retaliation mission in the region for the UFO's race.</summary>
    void StartRetaliation(string region, DogfightUfo ufo);

    /// <summary>Adds X-COM activity to the first country and first region containing the UFO.</summary>
    void AddXcomActivity(DogfightUfo ufo, int score);

    /// <summary>SavedGame::getId.</summary>
    int NextId(string key);

    /// <summary>The interrupt percentage of the UFO's wave (Ufo::setDamage), or 0.</summary>
    int WaveInterruptPercentage(DogfightUfo ufo);

    /// <summary>AlienMission::setInterrupted(true).</summary>
    void InterruptMission(DogfightUfo ufo);
}
