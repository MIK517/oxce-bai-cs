using System.Text;
using Oxce.Mods;
using Oxce.Mods.Loading;
using Oxce.Mods.Resources;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class ResourceDescriptorResolverTests
{
    [Fact]
    public void ResolvesSharedAndModAllocatedIndicesWithImmutableGenerationHandles()
    {
        using var fixture = ResourceModFixture.Create(
            """
            extraSprites:
              - type: SET.PCK
                width: 32
                height: 40
                files: {1: Resources/shared.png, 2: Resources/master.png}
            extraSounds:
              - type: SET.CAT
                files: {1: Resources/master.wav}
            """,
            """
            extraSprites:
              - type: SET.PCK
                width: 32
                height: 40
                files: {1: Resources/override.png, 2: Resources/addon.png}
            extraSounds:
              - type: SET.CAT
                files: {1: Resources/addon.wav}
            """);
        fixture.WriteMaster("Resources/shared.png", "shared");
        fixture.WriteMaster("Resources/master.png", "master");
        fixture.WriteMaster("Resources/master.wav", "master-sound");
        fixture.WriteAddon("Resources/addon.png", "addon");
        fixture.WriteAddon("Resources/override.png", "override");
        fixture.WriteAddon("Resources/addon.wav", "addon-sound");

        var snapshot = ContentSnapshotBuilder.Build(fixture.CreatePlan(), options: new ContentSnapshotOptions
        {
            ResourceResolution = new ResourceResolutionOptions
            {
                SharedSpriteCounts = new Dictionary<string, int> { ["SET.PCK"] = 2 },
                SharedSoundCounts = new Dictionary<string, int> { ["SET.CAT"] = 1 },
            },
        });

        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.ResourcesResolved));
        Assert.Empty(snapshot.Diagnostics);
        Assert.Equal([1, 2, 1002], snapshot.Content.Resources.Descriptors
            .Where(descriptor => descriptor.Kind == ResourceKind.Sprite)
            .Select(descriptor => descriptor.RuntimeIndex));
        Assert.Equal("resource-addon", snapshot.Content.Resources.Descriptors
            .Single(descriptor => descriptor.Kind == ResourceKind.Sprite && descriptor.RuntimeIndex == 1)
            .Provenance.ModId);
        Assert.Equal([1, 1001], snapshot.Content.Resources.Descriptors
            .Where(descriptor => descriptor.Kind == ResourceKind.Sound)
            .Select(descriptor => descriptor.RuntimeIndex));
        foreach (var descriptor in snapshot.Content.Resources.Descriptors)
        {
            Assert.Equal(snapshot.Content.Resources.Generation, descriptor.Handle.Generation);
            Assert.Same(descriptor, snapshot.Content.Resources[descriptor.Handle]);
        }
    }

    [Fact]
    public void SubdividedSpritesResolveEveryCellAndGlobeMarkersUseModOffsets()
    {
        using var fixture = ResourceModFixture.Create(
            """
            extraSprites:
              - type: GlobeMarkers
                width: 27
                height: 3
                subX: 3
                subY: 3
                files: {0: Resources/markers.png}
            """,
            """
            extraSprites:
              - type: GlobeMarkers
                width: 6
                height: 3
                subX: 3
                subY: 3
                files: {9: Resources/extra.png}
            crafts:
              - type: CRAFT
                marker: 9
              - type: PLAIN
            ufos:
              - type: UFO
                marker: 10
                markerLand: 2
                markerCrash: 11
            """);
        fixture.WriteMaster("Resources/markers.png", "markers");
        fixture.WriteAddon("Resources/extra.png", "extra");

        var snapshot = ContentSnapshotBuilder.Build(fixture.CreatePlan());

        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked),
            string.Join(Environment.NewLine, snapshot.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var markers = snapshot.Content.Resources.Descriptors
            .Where(static descriptor => descriptor.OwnerId == "GlobeMarkers")
            .OrderBy(static descriptor => descriptor.RuntimeIndex)
            .ToArray();
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 1009, 1010], markers.Select(static descriptor => descriptor.RuntimeIndex));
        Assert.All(markers, static descriptor => Assert.Equal((3, 3), (descriptor.Width, descriptor.Height)));
        Assert.Equal(new ResourceSubFrame(24, 0, 3, 3), markers[8].SubFrame);
        Assert.Equal(new ResourceSubFrame(3, 0, 3, 3), markers[10].SubFrame);
        Assert.Equal("Resources/extra.png", markers[10].CanonicalPath, ignoreCase: true);
        Assert.True(snapshot.Content.Resources.TryResolveIndex(ResourceKind.Sprite, "GlobeMarkers", "resource-addon", 10,
            out var resolved));
        Assert.Equal(1010, resolved!.RuntimeIndex);

        var rules = snapshot.Content.RuntimeRules;
        Assert.Equal(1009, rules.Crafts[rules.Crafts.GetRequired("CRAFT")].Value.Marker);
        Assert.Equal(-1, rules.Crafts[rules.Crafts.GetRequired("PLAIN")].Value.Marker);
        var ufo = rules.Ufos[rules.Ufos.GetRequired("UFO")].Value;
        // Mod::getOffset(marker, 8): shared frames keep their number; any frame above them is offset,
        // even one the mod does not declare.
        Assert.Equal((1010, 2, 1011), (ufo.Marker, ufo.LandedMarker, ufo.CrashedMarker));
    }

    [Theory]
    [InlineData("width: 32768\n    height: 32768\n    subX: 1\n    subY: 1", "Resources/cells.png")]
    [InlineData("width: 6\n    height: 3\n    subX: 3\n    subY: 3", "Resources/missing.png")]
    public void InvalidSubdividedDeclarationsReportOnceInsteadOfPerCell(string size, string file)
    {
        using var fixture = ResourceModFixture.Create(
            "extraSprites:\n  - type: CELLS\n    " + size + "\n    files: {0: " + file + "}\n", "");
        fixture.WriteMaster("Resources/cells.png", "cells");

        var snapshot = ContentSnapshotBuilder.Build(fixture.CreatePlan());

        Assert.False(snapshot.Capabilities.Has(ContentLoadStage.ResourcesResolved));
        Assert.Single(snapshot.Diagnostics, diagnostic => diagnostic.Context.RuleType == "extraSprites");
        Assert.DoesNotContain(snapshot.Content.Resources.Descriptors, descriptor => descriptor.OwnerId == "CELLS");
    }

    [Fact]
    public void MissingAndOversizedDeclarationsPreventResourceCapabilityWithProvenance()
    {
        using var fixture = ResourceModFixture.Create(
            """
            extraSprites:
              - type: BAD.PCK
                width: 50000
                height: 40
                files: {0: Resources/missing.png}
            extraSounds:
              - type: BAD.CAT
                files: {0: Resources/missing.wav}
            """,
            string.Empty);

        var snapshot = ContentSnapshotBuilder.Build(fixture.CreatePlan());

        Assert.False(snapshot.Capabilities.Has(ContentLoadStage.ResourcesResolved));
        Assert.Contains(snapshot.Diagnostics, item => item.Code == ModDiagnosticCodes.InvalidResourceDescriptor &&
            item.Context.ModId == "resource-master" && item.Context.RuleType == "extraSprites");
        Assert.Contains(snapshot.Diagnostics, item => item.Code == ModDiagnosticCodes.MissingDeclaredResource &&
            item.Context.ModId == "resource-master" && item.Context.RuleType == "extraSounds");
    }

    [Fact]
    public void IndexOutsideReservedModRangeFailsIntentionally()
    {
        using var fixture = ResourceModFixture.Create(
            """
            extraSounds:
              - type: BAD.CAT
                files: {1000: Resources/sound.wav}
            """,
            string.Empty);
        fixture.WriteMaster("Resources/sound.wav", "sound");

        var snapshot = ContentSnapshotBuilder.Build(fixture.CreatePlan());

        Assert.False(snapshot.Capabilities.Has(ContentLoadStage.ResourcesResolved));
        var error = Assert.Single(snapshot.Diagnostics,
            item => item.Code == ModDiagnosticCodes.InvalidResourceDescriptor);
        Assert.Contains("reserved range", error.Message, StringComparison.Ordinal);
        Assert.Equal("resource-master", error.Context.ModId);
    }

    private sealed class ResourceModFixture : IDisposable
    {
        private ResourceModFixture(string masterRules, string addonRules)
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-resource-resolution-{Guid.NewGuid():N}");
            Master = CreateMod("resource-master", masterRules, isMaster: true);
            Addon = CreateMod("resource-addon", addonRules, isMaster: false);
        }

        public string Root { get; }
        private string Master { get; }
        private string Addon { get; }

        public static ResourceModFixture Create(string masterRules, string addonRules) => new(masterRules, addonRules);

        public void WriteMaster(string path, string contents) => Write(Master, path, contents);
        public void WriteAddon(string path, string contents) => Write(Addon, path, contents);

        public ModLoadPlan CreatePlan() =>
            TestFixtures.CreatePlan(Root, "resource-master", ["resource-master", "resource-addon"]);

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private string CreateMod(string id, string rules, bool isMaster)
        {
            var mod = Path.Combine(Root, id);
            Directory.CreateDirectory(Path.Combine(mod, "Ruleset"));
            var metadata = $"id: {id}\nname: {id}\nversion: 1.0\nisMaster: {isMaster.ToString().ToLowerInvariant()}\nreservedSpace: 1\n";
            if (!isMaster) metadata += "master: resource-master\n";
            File.WriteAllText(Path.Combine(mod, "metadata.yml"), metadata, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(mod, "Ruleset", "fixture.rul"), rules, new UTF8Encoding(false));
            return mod;
        }

        private static void Write(string mod, string relativePath, string contents)
        {
            var path = Path.Combine(mod, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents, new UTF8Encoding(false));
        }
    }
}
