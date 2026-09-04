using Oxce.Mods.Bootstrap;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Formats.Yaml;
using Oxce.Scripting.Runtime;
using System.IO.Compression;
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
                InstallationLoadStage.CacheLookup,
                InstallationLoadStage.Parsing,
                InstallationLoadStage.Composition,
                InstallationLoadStage.TypeAndLink,
                InstallationLoadStage.ResourceResolution,
                InstallationLoadStage.ScriptCompilation,
                InstallationLoadStage.RuntimeRuleLinking,
                InstallationLoadStage.Completed,
            ],
            progress.Stages);
        Assert.Equal(CompiledContentCacheStatus.Miss, result.CacheStatus);
    }

    [Fact]
    public void WarmLoadRestoresPersistentContentWithFreshGeneration()
    {
        using var installation = new TemporaryInstallation();
        var request = installation.Request(addOnId: "runtime-addon");

        var first = InstallationContentLoader.Load(
            request,
            cancellationToken: TestContext.Current.CancellationToken);
        var second = InstallationContentLoader.Load(
            request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(first.IsSuccess, first.DescribeFailure());
        Assert.True(second.IsSuccess, second.DescribeFailure());
        Assert.Equal(CompiledContentCacheStatus.Miss, first.CacheStatus);
        Assert.True(second.CacheStatus == CompiledContentCacheStatus.Hit, second.CacheRejectionReason);
        Assert.NotEqual(first.Content!.Resources.Generation, second.Content!.Resources.Generation);
        Assert.Equal(first.Content.ParsedFileCount, second.Content.ParsedFileCount);
        Assert.Equal(first.Content.Scripts.Count, second.Content.Scripts.Count);
        Assert.Equal(first.Content.Resources.Descriptors.Count, second.Content.Resources.Descriptors.Count);
        Assert.Equal(first.Content.RuntimeRules.Items.Count, second.Content.RuntimeRules.Items.Count);
        Assert.Equal(
            first.Diagnostics.Select(static item => (item.Code, item.Severity, item.Message)),
            second.Diagnostics.Select(static item => (item.Code, item.Severity, item.Message)));
        Assert.Empty(Directory.EnumerateFiles(installation.CacheDirectory, "*.tmp"));
    }

    [Fact]
    public void DisabledCacheDoesNotCreateCacheStorage()
    {
        using var installation = new TemporaryInstallation();

        var result = InstallationContentLoader.Load(
            installation.Request(addOnId: "runtime-addon"),
            new InstallationContentLoadOptions
            {
                Cache = new CompiledContentCacheOptions { Enabled = false },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.DescribeFailure());
        Assert.Equal(CompiledContentCacheStatus.Disabled, result.CacheStatus);
        Assert.False(Directory.Exists(installation.CacheDirectory));
    }

    [Fact]
    public void ChangedInputAndCorruptPayloadFallBackToFreshBuild()
    {
        using var installation = new TemporaryInstallation();
        var request = installation.Request(addOnId: "runtime-addon");
        var first = InstallationContentLoader.Load(
            request,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess, first.DescribeFailure());

        File.AppendAllText(installation.FirstRuleset, Environment.NewLine);
        var changed = InstallationContentLoader.Load(
            request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(changed.IsSuccess, changed.DescribeFailure());
        Assert.Equal(CompiledContentCacheStatus.Rejected, changed.CacheStatus);

        File.WriteAllText(installation.CacheFile, "not a compiled content cache");
        var corrupt = InstallationContentLoader.Load(
            request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(corrupt.IsSuccess, corrupt.DescribeFailure());
        Assert.Equal(CompiledContentCacheStatus.Rejected, corrupt.CacheStatus);
    }

    [Fact]
    public void StructurallyIncompleteCompressedPayloadFallsBackToFreshBuild()
    {
        using var installation = new TemporaryInstallation();
        var request = installation.Request(addOnId: "runtime-addon");
        var first = InstallationContentLoader.Load(
            request,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess, first.DescribeFailure());

        var header = new byte[73];
        using (var input = File.OpenRead(installation.CacheFile))
        {
            input.ReadExactly(header);
        }
        using (var output = File.Create(installation.CacheFile))
        {
            output.Write(header);
            using var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true);
            using var writer = new StreamWriter(gzip);
            writer.Write("{\"formatVersion\":1}");
        }
        var recovered = InstallationContentLoader.Load(
            request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(recovered.IsSuccess, recovered.DescribeFailure());
        Assert.Equal(CompiledContentCacheStatus.Rejected, recovered.CacheStatus);
    }

    [Fact]
    public void CachedContentPreservesScriptScopesAndDeferredCompatibilityNodes()
    {
        using var installation = new TemporaryInstallation("content-ownership");
        var request = installation.Request("ownership-master", "ownership-addon");
        var first = InstallationContentLoader.Load(
            request,
            cancellationToken: TestContext.Current.CancellationToken);
        var cached = InstallationContentLoader.Load(
            request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(first.IsSuccess, first.DescribeFailure());
        Assert.True(cached.IsSuccess, cached.DescribeFailure());
        Assert.True(cached.CacheStatus == CompiledContentCacheStatus.Hit, cached.CacheRejectionReason);
        Assert.Equal(0, ExecuteSpriteScript(cached.Content!, "MASTER_ONLY"));
        Assert.Equal(1, ExecuteSpriteScript(cached.Content!, "ADDON_ONLY"));
        Assert.True(cached.Content!.Catalog.Items.Items.TryGet("SHARED_ITEM", out var shared));
        var deferred = Assert.Single(shared!.DeferredProperties,
            static property => property.Key == "customCompatibilityPayload" &&
                               property.Source.ModId == "ownership-addon");
        var mapping = Assert.IsType<YamlMappingNode>(deferred.Node);
        Assert.True(mapping.TryGet("nested", out var nested));
        Assert.Equal("addon", YamlValueReader.ReadString(nested!));
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

    private static int ExecuteSpriteScript(RuntimeContent content, string ownerId)
    {
        var artifact = Assert.Single(content.Scripts,
            script => script.OwnerId == ownerId && script.ParserName == "selectItemSprite");
        var result = ScriptVm.Execute(
            artifact.Program,
            new Dictionary<string, int> { ["sprite_index"] = 0 });
        Assert.True(result.Succeeded);
        return result.Outputs["sprite_index"];
    }

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
        public TemporaryInstallation(string fixtureName = "runtime-rule-linking")
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-install-loader-{Guid.NewGuid():N}");
            var standard = Path.Combine(Root, "standard");
            Directory.CreateDirectory(standard);
            Directory.CreateDirectory(Path.Combine(Root, "user", "mods"));
            CopyDirectory(
                Path.Combine(FindRepositoryRoot(), "fixtures", "public", "mods", fixtureName),
                standard);
        }

        public string Root { get; }

        public InstallationLoadRequest Request(string addOnId = "-") =>
            Request("runtime-master", addOnId);

        public InstallationLoadRequest Request(string masterId, string addOnId) =>
            InstallationLoadRequest.ForMasterAndAddOn(
                Root, masterId, addOnId, EngineIdentity());

        public string CacheDirectory => Path.Combine(Root, "user", "cache", "compiled-content");

        public string CacheFile => Path.Combine(CacheDirectory, "content-v1.json.gz");

        public string FirstRuleset => Directory.EnumerateFiles(
            Path.Combine(Root, "standard"), "*.rul", SearchOption.AllDirectories).First();

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
