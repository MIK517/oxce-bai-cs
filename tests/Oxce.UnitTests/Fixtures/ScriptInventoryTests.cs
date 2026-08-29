using System.Text.Json;
using Oxce.Scripting;
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

        Assert.Equal(2, inventory.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15",
            inventory.GetProperty("reference").GetProperty("commit").GetString());
        Assert.Equal(54, inventory.GetProperty("core").GetProperty("builtInOperations").GetArrayLength());
        Assert.Equal(24, Registration("scriptRegisterDefinitions").GetArrayLength());
        Assert.Equal(503, Registration("bindingNameCandidates").GetArrayLength());
        Assert.Equal(99, Registration("constantNameCandidates").GetArrayLength());
        Assert.Equal(35, Registration("parserTypes").GetArrayLength());
        Assert.Equal(27, Registration("scriptValueOwners").GetArrayLength());

        var limits = inventory.GetProperty("core").GetProperty("limits");
        Assert.Equal(9, limits.GetProperty("ScriptMaxOut").GetInt32());
        Assert.Equal(16, limits.GetProperty("ScriptMaxArg").GetInt32());
        Assert.Equal(64, limits.GetProperty("ScriptMaxRegPointerFactor").GetInt32());
        Assert.Equal(256, limits.GetProperty("EventsMax").GetInt32());
        Assert.Equal(100, limits.GetProperty("EventOffsetScale").GetInt32());
        Assert.Equal(10_000, limits.GetProperty("EventOffsetMax").GetInt32());
        Assert.Equal(ScriptLimits.MaximumOutputs, limits.GetProperty("ScriptMaxOut").GetInt32());
        Assert.Equal(ScriptLimits.MaximumArguments, limits.GetProperty("ScriptMaxArg").GetInt32());
        Assert.Equal(ScriptLimits.RegisterPointerFactor,
            limits.GetProperty("ScriptMaxRegPointerFactor").GetInt32());

        var encoding = inventory.GetProperty("core").GetProperty("typeEncoding");
        Assert.Equal(16, encoding.GetProperty("baseStep").GetInt32());
        Assert.Equal(32, encoding.GetProperty("int").GetInt32());
        Assert.Equal(96, encoding.GetProperty("firstCustom").GetInt32());
        Assert.Equal(12, encoding.GetProperty("modifiers").GetProperty("editablePointer").GetInt32());

        var macroOperations = inventory.GetProperty("core").GetProperty("macroOperations");
        var directRegistrations = inventory.GetProperty("core").GetProperty("directRegistrations");
        Assert.Equal(40, macroOperations.GetArrayLength());
        Assert.Equal(17, directRegistrations.GetArrayLength());

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
