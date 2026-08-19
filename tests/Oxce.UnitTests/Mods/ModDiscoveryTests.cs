using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class ModDiscoveryTests
{
    [Fact]
    public void MalformedMissingAndDuplicateMetadataAreRejectedWithDiagnostics()
    {
        using var fixture = new TemporaryModDirectory();
        fixture.Add("a-first", "id: duplicate\nisMaster: true\n");
        fixture.Add("b-duplicate", "id: duplicate\n");
        fixture.Add("c-malformed", "id: [unterminated\n");
        Directory.CreateDirectory(Path.Combine(fixture.Path, "d-missing"));
        var diagnostics = new DiagnosticCollector();

        var result = ModDiscovery.ScanDirectory(fixture.Path, diagnostics);

        Assert.Single(result.Mods);
        Assert.Equal(3, result.RejectedCount);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.DuplicateId);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.InvalidMetadata);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.MissingMetadata);
    }

    [Fact]
    public void MetadataFilenameUsesVirtualCatalogCaseRules()
    {
        using var fixture = new TemporaryModDirectory();
        fixture.Add("case-mod", "id: case-mod\nisMaster: true\n", "Metadata.YML");

        var result = ModDiscovery.ScanDirectory(fixture.Path);

        Assert.Equal("case-mod", Assert.Single(result.Mods).Metadata.Id);
    }

    private sealed class TemporaryModDirectory : IDisposable
    {
        public TemporaryModDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"oxce-mod-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Add(string directoryName, string metadata, string metadataFilename = "metadata.yml")
        {
            var directory = System.IO.Path.Combine(Path, directoryName);
            Directory.CreateDirectory(directory);
            File.WriteAllText(System.IO.Path.Combine(directory, metadataFilename), metadata);
            File.WriteAllText(System.IO.Path.Combine(directory, "content.rul"), "marker: true\n");
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
