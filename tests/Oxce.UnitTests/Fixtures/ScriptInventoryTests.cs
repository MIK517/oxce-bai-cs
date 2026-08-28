using System.Text.Json;
using Xunit;

namespace Oxce.UnitTests.Fixtures;

public sealed class ScriptInventoryTests
{
    [Fact]
    public void CommittedInventoryIsPinnedSortedAndAuditable()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(root, "docs", "compatibility", "script-inventory.json")));
        var inventory = document.RootElement;

        Assert.Equal(1, inventory.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15",
            inventory.GetProperty("reference").GetProperty("commit").GetString());
        Assert.Equal(54, inventory.GetProperty("core").GetProperty("builtInOperations").GetArrayLength());
        Assert.Equal(24, Registration("scriptRegisterDefinitions").GetArrayLength());
        Assert.Equal(503, Registration("bindingNameCandidates").GetArrayLength());
        Assert.Equal(99, Registration("constantNameCandidates").GetArrayLength());
        Assert.Equal(35, Registration("parserTypes").GetArrayLength());
        Assert.Equal(27, Registration("scriptValueOwners").GetArrayLength());

        var operations = inventory.GetProperty("core").GetProperty("builtInOperations")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Equal(operations.Order(StringComparer.Ordinal), operations);
        Assert.Equal(operations.Distinct(StringComparer.Ordinal), operations);

        JsonElement Registration(string name) =>
            inventory.GetProperty("registrations").GetProperty(name);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
            directory = directory.Parent;
        return directory?.FullName ??
            throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
