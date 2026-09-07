namespace Oxce.FixtureSupport;

public static class FixturePaths
{
    public static string FindRepositoryRoot(string? startPath = null)
    {
        var directory = new DirectoryInfo(startPath ?? AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
