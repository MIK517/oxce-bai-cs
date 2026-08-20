using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Files;
using Oxce.Mods.Loading;
using Oxce.Mods.Metadata;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class ModActivationReconcilerTests
{
    [Fact]
    public void ReconcilesMissingModsAndMultipleMastersWhilePreservingUserOrder()
    {
        var catalog = ModCatalog.Create(
            [Candidate("xcom1", isMaster: true), Candidate("xcom2", isMaster: true), Candidate("addon", master: "xcom1")]);
        var diagnostics = new DiagnosticCollector();

        var state = ModActivationReconciler.Reconcile(
            catalog,
            [
                new ModActivation("missing", true),
                new ModActivation("xcom2", true),
                new ModActivation("addon", true),
                new ModActivation("xcom1", true),
            ],
            diagnostics: diagnostics);

        Assert.Equal("xcom2", state.ActiveMasterId);
        Assert.Equal(["xcom2", "xcom1", "addon"], state.Activations.Select(item => item.Id));
        Assert.Equal([true, false, true], state.Activations.Select(item => item.Enabled));
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.MissingActivation);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.MultipleActiveMasters);
    }

    [Fact]
    public void PreferredMasterOverridesPersistedFlagsAndNewModsDefaultDisabled()
    {
        var catalog = ModCatalog.Create(
            [Candidate("xcom1", isMaster: true), Candidate("xcom2", isMaster: true), Candidate("new-addon", master: "xcom1")]);

        var state = ModActivationReconciler.Reconcile(
            catalog,
            [new ModActivation("xcom2", true)],
            preferredMasterId: "xcom1");

        Assert.Equal("xcom1", state.ActiveMasterId);
        Assert.Equal(
            [new ModActivation("xcom2", false), new ModActivation("xcom1", true), new ModActivation("new-addon", false)],
            state.Activations);
    }

    [Theory]
    [InlineData("Extended", "8.6.1.0", "Extended", "8.6.1", true)]
    [InlineData("Extended", "8.6.1.0", "Extended", "8.7", false)]
    [InlineData("Extended", "8.6.1.0", "Other", "1.0", false)]
    [InlineData("Extended", "8.6.1.0", "", "", true)]
    [InlineData("Extended", "8.6.1.0", "", "1", false)]
    [InlineData("Extended", "1.A.2", "Extended", "1.0.2.0", true)]
    public void EngineIdentityMatchesReferenceEngineAndFourComponentVersionRules(
        string currentEngine,
        string currentVersion,
        string requiredEngine,
        string requiredVersion,
        bool expected)
    {
        var identity = new ModEngineIdentity(currentEngine, currentVersion);
        var metadata = Metadata("mod", string.Empty, isMaster: true) with
        {
            RequiredExtendedEngine = requiredEngine,
            RequiredExtendedVersion = requiredVersion,
        };

        Assert.Equal(expected, identity.Supports(metadata));
    }

    [Fact]
    public void LoadPlanRejectsAnIncompatibleEngineRequirement()
    {
        var diagnostics = new DiagnosticCollector();
        var catalog = ModCatalog.Create(
            [Candidate("xcom1", isMaster: true), Candidate("addon", master: "xcom1", requiredEngine: "Other")]);

        var plan = ModLoadPlanner.Create(
            catalog,
            [new ModActivation("addon", true)],
            "xcom1",
            new ModEngineIdentity("Extended", "8.6.1.0"),
            diagnostics);

        Assert.False(plan.IsValid);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.RequiredExtendedEngine);
    }

    private static ModCandidate Candidate(
        string id,
        string master = "",
        bool isMaster = false,
        string requiredEngine = "")
    {
        var metadata = Metadata(id, master, isMaster) with
        {
            RequiredExtendedEngine = requiredEngine,
            RequiredExtendedVersion = requiredEngine.Length == 0 ? string.Empty : "1.0",
        };
        return new ModCandidate(
            metadata,
            VirtualFileLayer.FromEntries(new VirtualFileProvenance(id, id, id), []));
    }

    private static ModMetadata Metadata(string id, string master, bool isMaster) => new()
    {
        Path = Path.GetFullPath(id),
        Id = id,
        Name = id,
        Description = string.Empty,
        Author = string.Empty,
        MasterId = master,
        Version = ModVersion.Parse("1.0"),
        VersionDisplay = "1.0",
        IsMaster = isMaster,
        ReservedSpace = 1,
        RequiredExtendedEngine = string.Empty,
        RequiredExtendedVersion = string.Empty,
        ResourceConfigFile = string.Empty,
        ExternalResourceDirectories = [],
    };
}
