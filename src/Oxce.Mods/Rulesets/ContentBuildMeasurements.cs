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
    ContentBuildStageMeasurement ResourceResolution,
    ContentBuildStageMeasurement ScriptCompilation)
{
    public ContentBuildMeasurements WithResourceResolution(ContentBuildStageMeasurement measurement) =>
        this with { ResourceResolution = measurement };

    public ContentBuildMeasurements WithScriptCompilation(ContentBuildStageMeasurement measurement) =>
        this with { ScriptCompilation = measurement };
}
