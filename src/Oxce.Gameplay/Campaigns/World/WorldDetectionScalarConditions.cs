using Oxce.Scripting.Compilation;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Signed scalar projections of comparison edges; holes remain conservative intervals.</summary>
internal static class WorldDetectionScalarConditions
{
    internal static bool Refine(ScriptConditionKind condition, bool matches,
        ref (int Min, int Max) left, ref (int Min, int Max) right)
    {
        if (!matches) condition = condition switch
        {
            ScriptConditionKind.Equal => ScriptConditionKind.NotEqual,
            ScriptConditionKind.NotEqual => ScriptConditionKind.Equal,
            ScriptConditionKind.LessThan => ScriptConditionKind.GreaterThanOrEqual,
            ScriptConditionKind.LessThanOrEqual => ScriptConditionKind.GreaterThan,
            ScriptConditionKind.GreaterThan => ScriptConditionKind.LessThanOrEqual,
            ScriptConditionKind.GreaterThanOrEqual => ScriptConditionKind.LessThan,
            _ => condition,
        };
        if (condition is ScriptConditionKind.GreaterThan or ScriptConditionKind.GreaterThanOrEqual)
            return Refine(condition == ScriptConditionKind.GreaterThan ? ScriptConditionKind.LessThan :
                ScriptConditionKind.LessThanOrEqual, true, ref right, ref left);
        long minLeft = left.Min, maxLeft = left.Max, minRight = right.Min, maxRight = right.Max;
        switch (condition)
        {
            case ScriptConditionKind.Equal:
                minLeft = minRight = Math.Max(minLeft, minRight);
                maxLeft = maxRight = Math.Min(maxLeft, maxRight);
                break;
            case ScriptConditionKind.NotEqual:
                if (minLeft == maxLeft)
                {
                    if (minRight == minLeft) minRight++;
                    if (maxRight == minLeft) maxRight--;
                }
                if (minRight == maxRight)
                {
                    if (minLeft == minRight) minLeft++;
                    if (maxLeft == minRight) maxLeft--;
                }
                break;
            case ScriptConditionKind.LessThan:
                maxLeft = Math.Min(maxLeft, maxRight - 1);
                minRight = Math.Max(minRight, minLeft + 1);
                break;
            case ScriptConditionKind.LessThanOrEqual:
                maxLeft = Math.Min(maxLeft, maxRight);
                minRight = Math.Max(minRight, minLeft);
                break;
        }
        if (minLeft > maxLeft || minRight > maxRight) return false;
        left = ((int)minLeft, (int)maxLeft);
        right = ((int)minRight, (int)maxRight);
        return true;
    }
}
