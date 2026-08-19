using System.Xml.Linq;
using Xunit;

namespace Oxce.UnitTests.Architecture;

public sealed class ProjectDependencyTests
{
    private static readonly Dictionary<string, HashSet<string>> AllowedReferences =
        new(StringComparer.Ordinal)
        {
            ["Oxce.App"] = Set(
                "Oxce.Engine",
                "Oxce.Formats",
                "Oxce.Mods",
                "Oxce.Platform.Sdl",
                "Oxce.Rendering",
                "Oxce.Savegames"),
            ["Oxce.Core"] = Set(),
            ["Oxce.Engine"] = Set("Oxce.Core", "Oxce.Gameplay", "Oxce.Rendering"),
            ["Oxce.Formats"] = Set("Oxce.Core"),
            ["Oxce.Gameplay"] = Set("Oxce.Core", "Oxce.Mods", "Oxce.Scripting"),
            ["Oxce.Mods"] = Set("Oxce.Core", "Oxce.Formats", "Oxce.Scripting"),
            ["Oxce.Platform.Sdl"] = Set("Oxce.Core", "Oxce.Engine", "Oxce.Rendering"),
            ["Oxce.Rendering"] = Set("Oxce.Core"),
            ["Oxce.Savegames"] = Set("Oxce.Core", "Oxce.Formats", "Oxce.Gameplay", "Oxce.Mods"),
            ["Oxce.Scripting"] = Set("Oxce.Core"),
        };

    [Fact]
    public void ProductionProjectReferencesFollowTheDocumentedArchitecture()
    {
        var graph = ReadProductionGraph();

        Assert.Equal(AllowedReferences.Keys.Order(), graph.Keys.Order());
        foreach (var project in graph)
        {
            var allowed = AllowedReferences[project.Key];
            var unexpected = project.Value.Except(allowed, StringComparer.Ordinal).Order().ToArray();
            Assert.True(
                unexpected.Length == 0,
                $"Project '{project.Key}' has disallowed reference(s): {string.Join(", ", unexpected)}.");
        }

        Assert.Contains("Oxce.Gameplay", graph["Oxce.Savegames"]);
        Assert.DoesNotContain("Oxce.Savegames", graph["Oxce.Gameplay"]);
    }

    [Fact]
    public void ProductionProjectReferenceGraphIsAcyclic()
    {
        var graph = ReadProductionGraph();
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var project in graph.Keys)
        {
            Visit(project, graph, completed, active, path);
        }
    }

    private static void Visit(
        string project,
        Dictionary<string, HashSet<string>> graph,
        HashSet<string> completed,
        HashSet<string> active,
        List<string> path)
    {
        if (completed.Contains(project))
        {
            return;
        }

        if (!active.Add(project))
        {
            var cycleStart = path.IndexOf(project);
            var cycle = path.Skip(cycleStart).Append(project);
            Assert.Fail($"Production project reference cycle: {string.Join(" -> ", cycle)}.");
        }

        path.Add(project);
        foreach (var dependency in graph[project])
        {
            Assert.True(graph.ContainsKey(dependency), $"Production project '{dependency}' was not discovered.");
            Visit(dependency, graph, completed, active, path);
        }

        path.RemoveAt(path.Count - 1);
        active.Remove(project);
        completed.Add(project);
    }

    private static Dictionary<string, HashSet<string>> ReadProductionGraph()
    {
        var sourceDirectory = Path.Combine(FindRepositoryRoot(), "src");
        return Directory.GetFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories)
            .ToDictionary(
                projectPath => Path.GetFileNameWithoutExtension(projectPath)
                    ?? throw new InvalidOperationException($"Project '{projectPath}' has no file name."),
                ReadReferences,
                StringComparer.Ordinal);
    }

    private static HashSet<string> ReadReferences(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Project '{projectPath}' has no directory.");
        return XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(static include => include is not null)
            .Select(include => Path.GetFullPath(include!, projectDirectory))
            .Select(path => Path.GetFileNameWithoutExtension(path)
                ?? throw new InvalidOperationException($"Project reference '{path}' has no file name."))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static HashSet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
