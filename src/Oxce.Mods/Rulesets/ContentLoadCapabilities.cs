namespace Oxce.Mods.Rulesets;

[Flags]
public enum ContentLoadStage
{
    None = 0,
    Composed = 1 << 0,
    Typed = 1 << 1,
    Linked = 1 << 2,
    ResourcesResolved = 1 << 3,
    ScriptsCompiled = 1 << 4,
    RuntimeLinked = 1 << 5,
}

public readonly record struct ContentLoadCapabilities(ContentLoadStage Stages)
{
    public bool Has(ContentLoadStage stage) => (Stages & stage) == stage;

    public ContentLoadCapabilities AdvanceTo(ContentLoadStage stage)
    {
        if (!Enum.IsDefined(stage) || stage is ContentLoadStage.None)
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        var required = stage switch
        {
            ContentLoadStage.Composed => ContentLoadStage.Composed,
            ContentLoadStage.Typed => ContentLoadStage.Composed | ContentLoadStage.Typed,
            ContentLoadStage.Linked => ContentLoadStage.Composed | ContentLoadStage.Typed | ContentLoadStage.Linked,
            ContentLoadStage.ResourcesResolved => ContentLoadStage.Composed | ContentLoadStage.Typed |
                ContentLoadStage.Linked | ContentLoadStage.ResourcesResolved,
            ContentLoadStage.ScriptsCompiled => ContentLoadStage.Composed | ContentLoadStage.Typed |
                ContentLoadStage.Linked | ContentLoadStage.ScriptsCompiled,
            ContentLoadStage.RuntimeLinked => ContentLoadStage.Composed | ContentLoadStage.Typed |
                ContentLoadStage.Linked | ContentLoadStage.ResourcesResolved | ContentLoadStage.ScriptsCompiled |
                ContentLoadStage.RuntimeLinked,
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };
        return new ContentLoadCapabilities(Stages | required);
    }

    public static ContentLoadCapabilities Composed =>
        new(ContentLoadStage.Composed);
}
