using System.Text;
using Oxce.Mods.Files;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class VirtualFileCatalogTests
{
    [Fact]
    public void LaterLayersWinWhileSlicesPreserveEveryLayer()
    {
        var common = Layer(
            "common",
            null,
            ("GEOGRAPH/WORLD.DAT", "common-world"),
            ("Language/en-US.yml", "common-en"),
            ("Rules/10-base.rul", "common-rule"));
        var middle = Layer(
            "middle",
            "middle-mod",
            ("Language/fr-FR.yml", "middle-fr"),
            ("Rules/20-middle.RUL", "middle-rule"));
        var mod = Layer(
            "mod",
            "final-mod",
            ("geograph/world.dat", "mod-world"),
            ("GEOGRAPH/WORLD.dat", "mod-world-override"),
            ("LANGUAGE/EN-us.yml", "mod-en"),
            ("rules/30-mod.rul", "mod-rule"));
        var catalog = new VirtualFileCatalog([common, middle, mod]);

        var winner = catalog.GetRequired("GeOgRaPh\\WORLD.DAT");
        Assert.Equal("mod-world-override", winner.SourcePath);
        Assert.Equal("final-mod", winner.Provenance.ModId);
        Assert.Equal(
            new string?[] { "common-world", null, "mod-world-override" },
            catalog.GetSlice("GEOGRAPH/world.dat").Select(entry => entry?.SourcePath).ToArray());
        Assert.Equal(["en-us.yml", "fr-fr.yml"], catalog.List("LANGUAGE/"));
        Assert.Equal(["en-us.yml"], catalog.List("language", 0));
        Assert.Equal(["common-rule", "middle-rule", "mod-rule"],
            catalog.Rulesets.Select(entry => entry.SourcePath).ToArray());
        Assert.False(catalog.TryGet("rules/10-base.rul", out _));
    }

    [Fact]
    public void SameLayerCaseCollisionUsesLastEntryWithoutDuplicatingDirectoryName()
    {
        var layer = Layer(
            "collision",
            "collision-mod",
            ("GEOGRAPH/WORLD.DAT", "first-spelling"),
            ("geograph/world.dat", "last-spelling"));

        Assert.True(layer.TryGet("GeOgRaPh\\WoRlD.dAt", out var winner));
        Assert.Equal("last-spelling", winner?.SourcePath);
        Assert.Equal(["world.dat"], layer.List("GEOGRAPH"));
    }

    [Fact]
    public void DirectoryScanIsDeterministicBoundedAndReadable()
    {
        using var temporary = new TemporaryDirectory();
        var language = Directory.CreateDirectory(Path.Combine(temporary.Path, "Language"));
        File.WriteAllText(Path.Combine(language.FullName, "z.yml"), "z", Encoding.UTF8);
        File.WriteAllText(Path.Combine(language.FullName, "A.yml"), "a", Encoding.UTF8);
        File.WriteAllText(Path.Combine(temporary.Path, "root.dat"), "root", Encoding.UTF8);
        File.WriteAllText(Path.Combine(temporary.Path, "rules.rul"), "rules", Encoding.UTF8);

        var layer = VirtualFileLayer.ScanDirectory(temporary.Path, "disk", "disk-mod");
        var catalog = new VirtualFileCatalog([layer]);

        Assert.Equal(["a.yml", "z.yml"], catalog.List("language"));
        Assert.Single(catalog.Rulesets);
        using var reader = new StreamReader(catalog.GetRequired("ROOT.DAT").OpenRead(), Encoding.UTF8);
        Assert.Equal("root", reader.ReadToEnd());
        Assert.Throws<InvalidDataException>(() => VirtualFileLayer.ScanDirectory(
            temporary.Path,
            "limited",
            options: new DirectoryScanOptions { MaximumFiles = 1 }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/absolute.dat")]
    [InlineData("C:/rooted.dat")]
    [InlineData("folder//file.dat")]
    [InlineData("folder/./file.dat")]
    [InlineData("folder/../file.dat")]
    public void FilePathNormalizationRejectsAmbiguousOrUnsafePaths(string path)
    {
        Assert.Throws<ArgumentException>(() => VirtualPath.NormalizeFile(path));
    }

    [Fact]
    public void CatalogRejectsDuplicateLayerIds()
    {
        var first = Layer("duplicate", null, ("first.dat", "first"));
        var second = Layer("duplicate", "mod", ("second.dat", "second"));

        Assert.Throws<ArgumentException>(() => new VirtualFileCatalog([first, second]));
    }

    [Fact]
    public void DirectoryNormalizationAllowsTheVirtualRootButNotAnAbsoluteRoot()
    {
        Assert.Equal(string.Empty, VirtualPath.NormalizeDirectory(string.Empty));
        Assert.Equal("language", VirtualPath.NormalizeDirectory("LANGUAGE/"));
        Assert.Throws<ArgumentException>(() => VirtualPath.NormalizeDirectory("/"));
    }

    private static VirtualFileLayer Layer(
        string layerId,
        string? modId,
        params (string RelativePath, string SourcePath)[] sources) =>
        VirtualFileLayer.FromEntries(
            new VirtualFileProvenance(layerId, modId, $"fixture:{layerId}"),
            sources.Select(source => new VirtualFileSource(source.RelativePath, source.SourcePath)));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"oxce-vfs-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
