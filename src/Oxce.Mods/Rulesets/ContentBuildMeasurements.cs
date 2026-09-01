namespace Oxce.Mods.Rulesets;

public readonly record struct ContentBuildStageMeasurement(
    double ElapsedMilliseconds,
    long AllocatedBytes)
{
    public static ContentBuildStageMeasurement Empty { get; } = new(0, 0);
}

public sealed record ContentBuildMeasurements(
    ContentBuildStageMeasurement Parse,
    ContentBuildStageMeasurement Compose,
    ContentBuildStageMeasurement TypeAndLink,
    ContentBuildStageMeasurement ScriptCompilation)
{
    public ContentBuildMeasurements WithScriptCompilation(ContentBuildStageMeasurement measurement) =>
        this with { ScriptCompilation = measurement };
}
