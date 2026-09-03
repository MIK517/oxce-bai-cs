using Oxce.Mods.Bootstrap;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class InstallationContentLoaderTests
{
    [Fact]
    public void RuntimeLoadPublishesContentAndStableProgressStages()
    {
        using var installation = new TemporaryInstallation();
        var progress = new ProgressCollector();

        var result = InstallationContentLoader.Load(
            installation.Request(addOnId: "runtime-addon"),
            progress: progress,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.DescribeFailure());
        Assert.NotNull(result.Measurements);
        Assert.True(result.Content!.Capabilities.Has(ContentLoadStage.RuntimeLinked));
        Assert.Equal(
            [
                InstallationLoadStage.Discovery,
                InstallationLoadStage.Planning,
                InstallationLoadStage.Parsing,
                InstallationLoadStage.Composition,
                InstallationLoadStage.TypeAndLink,
                InstallationLoadStage.ResourceResolution,
                InstallationLoadStage.ScriptCompilation,
                InstallationLoadStage.RuntimeRuleLinking,
                InstallationLoadStage.Completed,
            ],
            progress.Stages);
    }

    [Fact]
    public void CancellationDuringContentBuildReturnsStructuredFailureBeforePublication()
    {
        using var installation = new TemporaryInstallation();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var progress = new CancellingProgress(InstallationLoadStage.Composition, cancellation);

        var result = InstallationContentLoader.Load(
            installation.Request(),
            progress: progress,
            cancellationToken: cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Content);
        Assert.Equal(InstallationLoadFailureKind.Cancelled, result.Failure!.Kind);
        Assert.Equal(InstallationLoadStage.Composition, result.Failure.Stage);
    }

    [Fact]
    public void MissingInstallationReturnsStructuredDiscoveryFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oxce-missing-install-{Guid.NewGuid():N}");
        var request = InstallationLoadRequest.ForMasterAndAddOn(
            root, "runtime-master", "-", EngineIdentity());

        var result = InstallationContentLoader.Load(
            request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(InstallationLoadFailureKind.Discovery, result.Failure!.Kind);
        Assert.Contains("standard", result.DescribeFailure(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoAddOnMarkerDoesNotBecomeAnActivation()
    {
        using var installation = new TemporaryInstallation();
        var request = installation.Request();

        var result = InstallationPlanBuilder.Create(
            request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.DescribeFailure());
        Assert.Equal(["runtime-master"], request.ActiveMods);
        Assert.Equal(["runtime-master"], result.Plan!.Groups.Select(static group => group.Mod.Metadata.Id));
    }

    private static ModEngineIdentity EngineIdentity() => new("Extended", "8.6.1.0");

    private sealed class ProgressCollector : IProgress<InstallationLoadProgress>
    {
        private readonly List<InstallationLoadStage> _stages = [];

        public IReadOnlyList<InstallationLoadStage> Stages => _stages;

        public void Report(InstallationLoadProgress value) => _stages.Add(value.Stage);
    }

    private sealed class CancellingProgress(
        InstallationLoadStage cancellationStage,
        CancellationTokenSource cancellation) : IProgress<InstallationLoadProgress>
    {
        public void Report(InstallationLoadProgress value)
        {
            if (value.Stage == cancellationStage) cancellation.Cancel();
        }
    }

    private sealed class TemporaryInstallation : IDisposable
    {
        public TemporaryInstallation()
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-install-loader-{Guid.NewGuid():N}");
            var standard = Path.Combine(Root, "standard");
            Directory.CreateDirectory(standard);
            Directory.CreateDirectory(Path.Combine(Root, "user", "mods"));
            CopyDirectory(
                Path.Combine(FindRepositoryRoot(), "fixtures", "public", "mods", "runtime-rule-linking"),
                standard);
        }

        public string Root { get; }

        public InstallationLoadRequest Request(string addOnId = "-") =>
            InstallationLoadRequest.ForMasterAndAddOn(
                Root, "runtime-master", addOnId, EngineIdentity());

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private static void CopyDirectory(string source, string destination)
        {
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
