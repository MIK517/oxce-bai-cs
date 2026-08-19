using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Files;
using Oxce.Mods.Loading;
using Oxce.Mods.Metadata;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class ModCatalogTests
{
    [Fact]
    public void MissingMasterRemovesDependentChain()
    {
        var diagnostics = new DiagnosticCollector();

        var catalog = ModCatalog.Create(
            [Candidate("broken", "missing"), Candidate("child", "broken"), Candidate("standalone", "")],
            diagnostics);

        Assert.Equal(["standalone"], catalog.Mods.Keys);
        Assert.Single(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.MissingMaster);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.DependentRemoved);
    }

    [Fact]
    public void CycleRemovesCycleAndItsDependents()
    {
        var diagnostics = new DiagnosticCollector();

        var catalog = ModCatalog.Create(
            [Candidate("a", "b"), Candidate("b", "a"), Candidate("child", "a"), Candidate("ok", "")],
            diagnostics);

        Assert.Equal(["ok"], catalog.Mods.Keys);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.DependencyCycle);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.DependentRemoved);
    }

    [Fact]
    public void FirstDiscoveredDuplicateWinsAndLaterCandidateIsDiagnosed()
    {
        var diagnostics = new DiagnosticCollector();
        var first = Candidate("same", "");
        var second = Candidate("same", "");

        var catalog = ModCatalog.Create([first, second], diagnostics);

        Assert.Same(first, catalog.Mods["same"]);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.DuplicateId);
    }

    private static ModCandidate Candidate(string id, string master) => new(
        new ModMetadata
        {
            Path = Path.GetFullPath(id),
            Id = id,
            Name = id,
            Description = string.Empty,
            Author = string.Empty,
            MasterId = master,
            Version = ModVersion.Parse("1.0"),
            VersionDisplay = "1.0",
            IsMaster = master.Length == 0,
            ReservedSpace = 1,
            RequiredExtendedEngine = string.Empty,
            RequiredExtendedVersion = string.Empty,
            ResourceConfigFile = string.Empty,
            ExternalResourceDirectories = [],
        },
        VirtualFileLayer.FromEntries(new VirtualFileProvenance(id, id, id), []));
}
