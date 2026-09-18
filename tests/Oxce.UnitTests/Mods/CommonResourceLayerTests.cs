using Oxce.Core.Diagnostics;
using Oxce.Mods.Discovery;
using Oxce.Mods.Files;
using Oxce.Mods.Loading;
using Xunit;

namespace Oxce.UnitTests.Mods;

/// <summary>
/// The reference engine maps <c>common</c> once for the whole installation, below every mod
/// (<c>FileMap::setup</c> calls <c>VFS::map_common</c> before pushing any mod record).
/// </summary>
public sealed class CommonResourceLayerTests
{
    [Fact]
    public void MasterWithoutExternalResourcesStillSeesCommonFiles()
    {
        using var installation = new TemporaryInstallation();

        var plan = installation.Plan("master");

        Assert.Equal(["common:directory"], plan.CommonLayers.Select(layer => layer.Provenance.LayerId));
        Assert.Equal("common", TemporaryInstallation.ReadText(plan, "Language/en-US.yml"));
        Assert.Equal("common", TemporaryInstallation.ReadText(plan, "Resources/common.dat"));
        Assert.Equal("master", TemporaryInstallation.ReadText(plan, "Resources/duel.dat"));
    }

    [Fact]
    public void CommonStaysBelowExternalGameDataAndEveryMod()
    {
        // Only a top-level master can declare external resources (ModInfo::load), so the master
        // owns the game data layer while common belongs to the installation.
        using var installation = new TemporaryInstallation(masterLoadsResources: true);

        var plan = installation.Plan("master", "addon");

        Assert.Equal(["master", "addon"], plan.Groups.Select(group => group.Mod.Metadata.Id));
        var files = plan.VirtualFiles;
        Assert.Equal(
            ["common:directory", "master:external:0:directory", "master", "addon"],
            files.Layers.Select(layer => layer.Provenance.LayerId));
        Assert.Equal("master", TemporaryInstallation.ReadText(plan, "Resources/duel.dat"));
        Assert.Equal("game", TemporaryInstallation.ReadText(plan, "Resources/game.dat"));
        Assert.Equal("common", TemporaryInstallation.ReadText(plan, "Resources/common.dat"));
        Assert.Equal("common", ReadText(files.GetSlice("Resources/duel.dat")[0]!));
    }

    [Fact]
    public void CommonIsMappedOnceWhenSeveralModDirectoriesAreScanned()
    {
        using var installation = new TemporaryInstallation();
        var diagnostics = new DiagnosticCollector();
        var options = installation.DiscoveryOptions;

        var mods = ModDiscovery.ScanDirectory(installation.ModsRoot, diagnostics, options);
        var extra = ModDiscovery.ScanDirectory(installation.ExtraModsRoot, diagnostics, options);
        var catalog = ModCatalog.Create(
            mods.Mods.Concat(extra.Mods),
            diagnostics,
            mods.CommonLayers.Concat(extra.CommonLayers));

        Assert.Equal(["common:directory"], catalog.CommonLayers.Select(layer => layer.Provenance.LayerId));
        var plan = ModLoadPlanner.Create(
            catalog,
            [new ModActivation("extra", true)],
            "master",
            TemporaryInstallation.Engine,
            diagnostics);
        Assert.Equal(
            ["common:directory", "master", "extra"],
            plan.VirtualFiles.Layers.Select(layer => layer.Provenance.LayerId));
        Assert.Equal("common", TemporaryInstallation.ReadText(plan, "Resources/common.dat"));
    }

    private static string ReadText(VirtualFileEntry entry)
    {
        using var reader = new StreamReader(entry.OpenRead());
        return reader.ReadToEnd();
    }

    private sealed class TemporaryInstallation : IDisposable
    {
        public static readonly ModEngineIdentity Engine = new("Extended", "8.6.1.0");

        public TemporaryInstallation(bool masterLoadsResources = false)
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-common-test-{Guid.NewGuid():N}");
            ModsRoot = Path.Combine(Root, "mods");
            ExtraModsRoot = Path.Combine(Root, "extra");
            Write("common/Language/en-US.yml", "common");
            Write("common/Resources/common.dat", "common");
            Write("common/Resources/duel.dat", "common");
            Write("UFO/Resources/game.dat", "game");
            Write("UFO/Resources/duel.dat", "game");
            Write(
                "mods/master/metadata.yml",
                "id: master\nname: Master\nversion: 1.0\nisMaster: true\n"
                    + (masterLoadsResources ? "loadResources: [UFO]\n" : string.Empty));
            Write("mods/master/Resources/master.dat", "master");
            Write("mods/master/Resources/duel.dat", "master");
            Write("mods/addon/metadata.yml", "id: addon\nname: Add-on\nmaster: master\n");
            Write("mods/addon/Resources/addon.dat", "addon");
            Write("extra/extra/metadata.yml", "id: extra\nname: Extra\nmaster: master\n");
            Write("extra/extra/Resources/extra.dat", "extra");
        }

        public string Root { get; }

        public string ModsRoot { get; }

        public string ExtraModsRoot { get; }

        public ModDiscoveryOptions DiscoveryOptions => new() { ExternalResourceRoots = [Root] };

        public ModLoadPlan Plan(string masterId, params string[] activeMods)
        {
            var diagnostics = new DiagnosticCollector();
            var discovery = ModDiscovery.ScanDirectory(ModsRoot, diagnostics, DiscoveryOptions);
            var catalog = ModCatalog.Create(discovery.Mods, diagnostics, discovery.CommonLayers);
            var activations = new[] { masterId }
                .Concat(activeMods)
                .Distinct(StringComparer.Ordinal)
                .Select(id => new ModActivation(id, true));
            var plan = ModLoadPlanner.Create(catalog, activations, masterId, Engine, diagnostics);
            Assert.True(plan.IsValid);
            return plan;
        }

        public static string ReadText(ModLoadPlan plan, string path) =>
            CommonResourceLayerTests.ReadText(plan.VirtualFiles.GetRequired(path));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private void Write(string relativePath, string contents)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }
    }
}
