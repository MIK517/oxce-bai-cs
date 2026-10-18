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

    /// <summary>The first video this tick queued, and the ending this tick set with the video that ends the game.</summary>
    private (CampaignTime Time, string Cutscene)? _tickCutscene;
    private (CampaignTime Time, string? Cutscene)? _tickEnding;

    /// <summary>
    /// GeoscapeState::time1Day's research cutscene: the video is queued and may end the campaign. Mod::getVideo throws for
    /// an undefined video; the port plays nothing (ADR 0036). The ending is reported by the tick's five-second level.
    /// </summary>
    private void PlayCutscene(string cutscene, TimeEffects effects)
    {
        if (cutscene.Length == 0 || !_content.RuntimeRules.Campaign.Cutscenes.TryGetValue(cutscene, out var video)) return;
        effects.Notify(new CampaignCutscenePlayed(cutscene));
        if (_tickCutscene?.Time != Time) _tickCutscene = (Time, cutscene);
        var before = Ending;
        EndByVideo(video);
        if (Ending != before) _tickEnding = (Time, _tickCutscene!.Value.Cutscene);
    }

    /// <summary>An ending set inside a tick (time5Seconds when no base is left).</summary>
    internal void EndInTick(int ending)
    {
        EndCampaign(ending);
        _tickEnding = (Time, null);
    }

    /// <summary>
    /// The end of a tick that ended the campaign: the game-over video (time5Seconds plays the defeat video at once for a
    /// loss; a win shows the first video queued), and the ironman save time1Day and time5Seconds push.
    /// </summary>
    internal void ReportTickEnding(TimeEffects effects)
    {
        if (_tickEnding is not { } ended || ended.Time != Time) return;
        _tickEnding = null;
        effects.Notify(new CampaignEnded(Ending)
        {
            Cutscene = Ending == 2 ? _content.RuntimeRules.Campaign.Monthly.LoseDefeatCutscene : ended.Cutscene,
        });
        if (Identity.Ironman) effects.Notify(new CampaignSaveRequested(CampaignSaveKind.Ironman));
    }
}
