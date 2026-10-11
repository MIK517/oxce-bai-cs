using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>RuleMissionScript::getDelay; zero random delay consumes no RNG.</summary>
internal static class WorldMissionScriptSelection
{
    public static int Delay(RuntimeMissionScriptRule rule, IRandomSource random) =>
        checked(rule.StartDelay + (rule.RandomDelay == 0 ? 0 : random.NextInclusive(0, rule.RandomDelay)));
}
