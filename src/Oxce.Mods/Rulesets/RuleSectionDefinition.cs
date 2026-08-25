namespace Oxce.Mods.Rulesets;

public sealed record RuleSectionDefinition
{
    public RuleSectionDefinition(string name, string identityKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityKey);
        Name = name;
        IdentityKey = identityKey;
    }

    public string Name { get; }

    public string IdentityKey { get; }
}
