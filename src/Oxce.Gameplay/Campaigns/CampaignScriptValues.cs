namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    internal int ScriptValue(string name) => _scriptValues?.Get(name) ?? 0;
}
