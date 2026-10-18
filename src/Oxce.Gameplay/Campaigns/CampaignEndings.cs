using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

/// <summary>CutsceneState for a video the campaign plays in a tick (research discoveries); its window is presentation.</summary>
public sealed record CampaignCutscenePlayed(string Cutscene) : ICampaignEvent;

public sealed partial class CampaignState
{
    /// <summary>
    /// D4, the one ending rule: while the campaign has no ending, a video that wins ends it won and one that loses ends
    /// it lost (a video with both loses, as the reference sets the win first). Null when the ending is unchanged.
    /// </summary>
    private CampaignEnded? EndByVideo(RuntimeCutscene video)
    {
        if (Ending != 0) return null;
        if (video.WinGame) EndCampaign(1);
        if (video.LoseGame) EndCampaign(2);
        return Ending != 0 ? new CampaignEnded(Ending) : null;
    }

    /// <summary>
    /// GeoscapeState::time1Day's research cutscene: the video plays and may end the campaign. Mod::getVideo throws for an
    /// undefined video; the port plays nothing (ADR 0036).
    /// </summary>
    private void PlayCutscene(string cutscene, TimeEffects effects)
    {
        if (cutscene.Length == 0 || !_content.RuntimeRules.Campaign.Cutscenes.TryGetValue(cutscene, out var video)) return;
        effects.Notify(new CampaignCutscenePlayed(cutscene));
        if (EndByVideo(video) is { } ended) effects.Notify(ended);
    }
}
