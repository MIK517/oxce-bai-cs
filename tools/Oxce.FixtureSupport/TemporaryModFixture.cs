namespace Oxce.FixtureSupport;

public sealed class TemporaryModFixture : IDisposable
{
    private const string DefaultMetadata =
        "id: fixture\nname: Fixture\nversion: 1.0\nisMaster: true\nreservedSpace: 1000\n";

    public TemporaryModFixture(string rules)
        : this(("fixture.rul", rules))
    {
    }

    public TemporaryModFixture(params (string Name, string Yaml)[] rulesets)
    {
        Root = Path.Combine(Path.GetTempPath(), $"oxce-mod-fixture-{Guid.NewGuid():N}");
        ModRoot = Path.Combine(Root, "fixture");
        var rulesetRoot = Path.Combine(ModRoot, "Ruleset");
        Directory.CreateDirectory(rulesetRoot);
        File.WriteAllText(Path.Combine(ModRoot, "metadata.yml"), DefaultMetadata);
        foreach (var (name, yaml) in rulesets)
        {
            WriteFile(Path.Combine("Ruleset", name), yaml);
        }
    }

    public string Root { get; }

    public string ModRoot { get; }

    public void WriteResource(string relativePath, string contents) => WriteFile(relativePath, contents);

    public void SetResourceConfig(string relativePath, string yaml)
    {
        File.AppendAllText(Path.Combine(ModRoot, "metadata.yml"), $"resourceConfig: {relativePath}\n");
        WriteFile(relativePath, yaml);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private void WriteFile(string relativePath, string contents)
    {
        var path = Path.Combine(ModRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
