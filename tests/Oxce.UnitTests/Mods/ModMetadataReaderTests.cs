using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Metadata;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class ModMetadataReaderTests
{
    [Fact]
    public void AppliesReferenceDefaultsAndClampsReservedSpace()
    {
        const string yaml = "reservedSpace: 101\n";
        var mapping = Assert.IsType<YamlMappingNode>(
            YamlCompatibilityReader.Parse(yaml, "metadata.yml").Documents[0].Root);

        var metadata = ModMetadataReader.Read(mapping, Path.Combine("root", "MyMod"));

        Assert.Equal("MyMod", metadata.Id);
        Assert.Equal("MyMod", metadata.Name);
        Assert.Equal("No description.", metadata.Description);
        Assert.Equal("unknown author", metadata.Author);
        Assert.Equal("xcom1", metadata.MasterId);
        Assert.Equal("1.0", metadata.Version.Text);
        Assert.Equal(100, metadata.ReservedSpace);
    }

    [Fact]
    public void TopLevelMasterLoadsExternalResourcesAndWildcardNormalizesToNoMaster()
    {
        const string yaml = "id: total\nisMaster: true\nmaster: '*'\nloadResources: [UFO, TFTD]\n";
        var mapping = Assert.IsType<YamlMappingNode>(
            YamlCompatibilityReader.Parse(yaml, "metadata.yml").Documents[0].Root);

        var metadata = ModMetadataReader.Read(mapping, Path.Combine("root", "total"));

        Assert.Empty(metadata.MasterId);
        Assert.Equal(["UFO", "TFTD"], metadata.ExternalResourceDirectories);
    }

    [Fact]
    public void InvalidVersionProducesLocatedStructuredDiagnostic()
    {
        const string yaml = "id: invalid\nversion: release 3\n";
        var mapping = Assert.IsType<YamlMappingNode>(
            YamlCompatibilityReader.Parse(yaml, "metadata.yml").Documents[0].Root);
        var diagnostics = new DiagnosticCollector();

        var metadata = ModMetadataReader.Read(mapping, Path.Combine("root", "invalid"), diagnostics);

        Assert.False(metadata.Version.IsValid);
        var diagnostic = Assert.Single(diagnostics.Snapshot());
        Assert.Equal(ModDiagnosticCodes.InvalidVersion, diagnostic.Code);
        Assert.Equal("invalid", diagnostic.Context.ModId);
        Assert.Equal("metadata.yml", diagnostic.Source?.SourceName);
        Assert.True(diagnostic.Source?.Start.Line > 1);
    }
}
