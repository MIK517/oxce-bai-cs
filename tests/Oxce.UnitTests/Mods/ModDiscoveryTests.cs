using System.IO.Compression;
using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Files;
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

    [Fact]
    public void DiscoversSingleAndMultiModArchivesAndOpensEntriesLazily()
    {
        using var fixture = new TemporaryModDirectory();
        fixture.AddArchive(
            "single.zip",
            ("metadata.yml", "id: single\nisMaster: true\n"),
            ("Resources/marker.txt", "single-content"));
        fixture.AddArchive(
            "multi.zip",
            ("master/", string.Empty),
            ("master/metadata.yml", "id: multi-master\nisMaster: true\n"),
            ("master/Ruleset/base.rul", "base: true\n"),
            ("addon/", string.Empty),
            ("addon/metadata.yml", "id: multi-addon\nmaster: multi-master\n"),
            ("addon/Resources/marker.txt", "addon-content"));

        var result = ModDiscovery.ScanDirectory(fixture.Path);

        Assert.Equal(3, result.Mods.Count);
        var single = Assert.Single(result.Mods, candidate => candidate.Metadata.Id == "single");
        Assert.True(single.Layer.TryGet("resources/marker.txt", out var entry));
        Assert.Contains("!", entry!.SourcePath, StringComparison.Ordinal);
        using var reader = new StreamReader(entry.OpenRead());
        Assert.Equal("single-content", reader.ReadToEnd());
        var addon = Assert.Single(result.Mods, candidate => candidate.Metadata.Id == "multi-addon");
        Assert.Equal("addon-content", ReadText(addon.Layer, "resources/marker.txt"));
        Assert.Single(result.Mods.Single(candidate => candidate.Metadata.Id == "multi-master").Layer.Rulesets);
    }

    [Fact]
    public void ArchiveWinsDuplicateIdAndUnsafeEntryIsIgnored()
    {
        using var fixture = new TemporaryModDirectory();
        fixture.Add("same-directory", "id: same\nisMaster: true\n");
        fixture.AddArchive(
            "same.zip",
            ("metadata.yml", "id: same\nisMaster: true\n"),
            ("archive.txt", "archive"),
            ("../outside.txt", "unsafe"));
        var diagnostics = new DiagnosticCollector();

        var result = ModDiscovery.ScanDirectory(fixture.Path, diagnostics);

        var candidate = Assert.Single(result.Mods);
        Assert.True(candidate.Layer.TryGet("archive.txt", out var archiveEntry));
        Assert.Contains("!", archiveEntry!.SourcePath, StringComparison.Ordinal);
        Assert.Equal(1, result.RejectedCount);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.DuplicateId);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.UnsafeArchiveEntry);
    }

    [Fact]
    public void MultiModArchiveRequiresReferenceTopLevelDirectoryEntries()
    {
        using var fixture = new TemporaryModDirectory();
        fixture.AddArchive(
            "implicit-directory.zip",
            ("hidden/metadata.yml", "id: hidden\nisMaster: true\n"),
            ("hidden/marker.txt", "content"));

        var result = ModDiscovery.ScanDirectory(fixture.Path);

        Assert.Empty(result.Mods);
        Assert.Equal(1, result.RejectedCount);
    }

    [Fact]
    public void ArchiveDirectorySpellingSuppliesFallbackMetadataId()
    {
        using var fixture = new TemporaryModDirectory();
        fixture.AddArchive(
            "case.zip",
            ("CaseMod/", string.Empty),
            ("CaseMod/metadata.yml", "isMaster: true\n"),
            ("CaseMod/marker.txt", "content"));

        var result = ModDiscovery.ScanDirectory(fixture.Path);

        Assert.Equal("CaseMod", Assert.Single(result.Mods).Metadata.Id);
    }

    [Fact]
    public void ExternalDirectoryOverridesArchiveAndOwningModOverridesBoth()
    {
        using var fixture = new TemporaryModDirectory();
        using var resources = new TemporaryModDirectory();
        fixture.Add(
            "master",
            "id: master\nisMaster: true\nloadResources: [UFO, SECOND]\n",
            content: [("GEODATA/shared.dat", "mod"), ("Ruleset/mod.rul", "mod: true\n")]);
        resources.AddResourceDirectory(
            "UFO",
            ("GEODATA/shared.dat", "directory"),
            ("GEODATA/loose.dat", "directory"),
            ("GEODATA/order.dat", "first-resource"));
        resources.AddResourceDirectory(
            "SECOND",
            ("GEODATA/order.dat", "second-resource"),
            ("GEODATA/second.dat", "second-resource"));
        resources.AddArchive(
            "UFO.zip",
            ("UFO/GEODATA/shared.dat", "archive"),
            ("UFO/GEODATA/loose.dat", "archive"),
            ("UFO/Ruleset/external.rul", "ignored: true\n"));
        var options = new ModDiscoveryOptions { ExternalResourceRoots = [resources.Path] };

        var result = ModDiscovery.ScanDirectory(fixture.Path, options: options);

        var candidate = Assert.Single(result.Mods);
        Assert.Equal(4, candidate.Layers.Count);
        var catalog = new VirtualFileCatalog(candidate.Layers);
        Assert.Equal("mod", ReadText(catalog.GetRequired("GEODATA/shared.dat")));
        Assert.Equal("directory", ReadText(catalog.GetRequired("GEODATA/loose.dat")));
        Assert.Equal("first-resource", ReadText(catalog.GetRequired("GEODATA/order.dat")));
        Assert.Equal(2, candidate.Layers.SelectMany(layer => layer.Rulesets).Count());
        Assert.DoesNotContain(
            candidate.Layers.SelectMany(layer => layer.Rulesets),
            entry => entry.SourcePath.Contains("external.rul", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingRequiredExternalResourceRejectsMaster()
    {
        using var fixture = new TemporaryModDirectory();
        fixture.Add("master", "id: master\nisMaster: true\nloadResources: [MISSING]\n");
        var diagnostics = new DiagnosticCollector();

        var result = ModDiscovery.ScanDirectory(fixture.Path, diagnostics);

        Assert.Empty(result.Mods);
        Assert.Equal(1, result.RejectedCount);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.MissingExternalResource);
    }

    [Fact]
    public void SharedCommonResourcesAreLowerPriorityThanGameAndModLayers()
    {
        using var fixture = new TemporaryModDirectory();
        using var resources = new TemporaryModDirectory();
        fixture.Add(
            "master",
            "id: master\nisMaster: true\nloadResources: [UFO]\n",
            content: [("Resources/shared.dat", "mod")]);
        resources.AddResourceDirectory("common",
            ("Resources/shared.dat", "common"), ("Resources/common.dat", "common"));
        resources.AddResourceDirectory("UFO",
            ("Resources/shared.dat", "game"), ("Resources/game.dat", "game"));

        var result = ModDiscovery.ScanDirectory(fixture.Path, options: new ModDiscoveryOptions
        {
            ExternalResourceRoots = [resources.Path],
        });

        var candidate = Assert.Single(result.Mods);
        var catalog = new VirtualFileCatalog(candidate.Layers);
        Assert.Equal("mod", ReadText(catalog.GetRequired("Resources/shared.dat")));
        Assert.Equal("common", ReadText(catalog.GetRequired("Resources/common.dat")));
        Assert.Equal("game", ReadText(catalog.GetRequired("Resources/game.dat")));
    }

    [Fact]
    public void OversizedExpandedArchiveEntryIsRejectedBeforeDiscovery()
    {
        using var fixture = new TemporaryModDirectory();
        fixture.AddArchive(
            "large.zip",
            ("metadata.yml", "id: large\nisMaster: true\n"),
            ("payload.bin", new string('x', 128)));
        var diagnostics = new DiagnosticCollector();
        var options = new ModDiscoveryOptions
        {
            ArchiveScan = new ZipArchiveScanOptions { MaximumEntryBytes = 64 },
        };

        var result = ModDiscovery.ScanDirectory(fixture.Path, diagnostics, options);

        Assert.Empty(result.Mods);
        Assert.Equal(1, result.RejectedCount);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.InvalidArchive);
    }

    private static string ReadText(VirtualFileLayer layer, string path)
    {
        Assert.True(layer.TryGet(path, out var entry));
        return ReadText(entry!);
    }

    private static string ReadText(VirtualFileEntry entry)
    {
        using var reader = new StreamReader(entry.OpenRead());
        return reader.ReadToEnd();
    }

    private sealed class TemporaryModDirectory : IDisposable
    {
        public TemporaryModDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"oxce-mod-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Add(
            string directoryName,
            string metadata,
            string metadataFilename = "metadata.yml",
            params (string Path, string Content)[] content)
        {
            var directory = System.IO.Path.Combine(Path, directoryName);
            Directory.CreateDirectory(directory);
            File.WriteAllText(System.IO.Path.Combine(directory, metadataFilename), metadata);
            File.WriteAllText(System.IO.Path.Combine(directory, "content.rul"), "marker: true\n");
            foreach (var item in content)
            {
                WriteFile(directory, item.Path, item.Content);
            }
        }

        public void AddResourceDirectory(string directoryName, params (string Path, string Content)[] content)
        {
            var directory = System.IO.Path.Combine(Path, directoryName);
            Directory.CreateDirectory(directory);
            foreach (var item in content)
            {
                WriteFile(directory, item.Path, item.Content);
            }
        }

        public void AddArchive(string filename, params (string Path, string Content)[] entries)
        {
            using var archive = ZipFile.Open(System.IO.Path.Combine(Path, filename), ZipArchiveMode.Create);
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Path);
                if (item.Path.EndsWith('/'))
                {
                    continue;
                }

                using var writer = new StreamWriter(entry.Open());
                writer.Write(item.Content);
            }
        }

        private static void WriteFile(string root, string relativePath, string content)
        {
            var path = System.IO.Path.Combine(root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
