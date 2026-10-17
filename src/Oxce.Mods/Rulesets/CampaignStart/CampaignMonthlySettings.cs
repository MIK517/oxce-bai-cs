namespace Oxce.Mods.Rulesets.CampaignStart;

/// <summary>
/// Global rules read by the month rollover and the monthly report (<c>Mod::loadFile</c>), with the
/// <c>Mod::Mod</c> defaults. Scalars layer per mod; each <c>monthlyRatings</c> mapping replaces the previous
/// one (the reference's map read clears the collection first) and keeps the first entry of a repeated key.
/// <c>defeatScore</c>, <c>defeatFunds</c> and <c>monthlyRatingThresholds</c> are read with the other global
/// rules; see <see cref="CampaignStartSettings"/> and <see cref="CampaignInterceptionSettings"/>.
/// </summary>
public sealed record CampaignMonthlySettings(
    int ChanceToDetectAlienBaseEachMonth,
    string LoseMoneyCutscene,
    string LoseRatingCutscene,
    string LoseDefeatCutscene,
    IReadOnlyList<CampaignMonthlyRating> MonthlyRatings)
{
    public static CampaignMonthlySettings Default { get; } = new CampaignMonthlySettingsBuilder().Build();
}

/// <summary>One <c>monthlyRatings</c> entry: the rating text shown from <paramref name="Score"/> upwards.</summary>
public sealed record CampaignMonthlyRating(int Score, string Text);

internal sealed class CampaignMonthlySettingsBuilder
{
    public int ChanceToDetectAlienBaseEachMonth { get; set; } = 20;
    public string LoseMoneyCutscene { get; set; } = "loseGame";
    public string LoseRatingCutscene { get; set; } = "loseGame";
    public string LoseDefeatCutscene { get; set; } = "loseGame";
    // std::map<int, std::string>: ordered by score.
    public SortedDictionary<int, string> MonthlyRatings { get; set; } = [];

    public CampaignMonthlySettings Build() => new(
        ChanceToDetectAlienBaseEachMonth,
        LoseMoneyCutscene,
        LoseRatingCutscene,
        LoseDefeatCutscene,
        Array.AsReadOnly(MonthlyRatings.Select(static pair => new CampaignMonthlyRating(pair.Key, pair.Value)).ToArray()));
}
