namespace Oxce.Mods.Rulesets;

/// <summary>A broken relationship between typed rules: the rule, its property, the related ID when one is named, and why.</summary>
public sealed record RuleValidationIssue(string RuleId, string Property, string? RelatedId, string Message);

/// <summary>The relationship issues a rule-family group found; valid when there are none.</summary>
public sealed record RuleValidation(IReadOnlyList<RuleValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
