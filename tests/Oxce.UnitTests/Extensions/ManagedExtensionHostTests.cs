using System.Text;
using Oxce.Core.Diagnostics;
using Oxce.Core.Random;
using Oxce.Extensions;
using Oxce.Extensions.Abstractions;
using Oxce.Gameplay.Campaigns;
using Oxce.TestExtension;
using Oxce.UnitTests.Gameplay;
using Xunit;

namespace Oxce.UnitTests.Extensions;

public sealed class ManagedExtensionHostTests
{
    [Fact]
    public void ManualExtensionLoadsExercisesCampaignAndCapturesBoundedState()
    {
        using var installation = new ExtensionInstallation();
        installation.Add("example.probe", typeof(ProbeExtension));
        var diagnostics = new DiagnosticCollector();
        using var host = ManagedExtensionHost.LoadFromDirectory(
            installation.Root, diagnostics, cancellationToken: TestContext.Current.CancellationToken);
        var campaign = CampaignFactory.Create(
            CampaignFoundationTests.LoadFixture(),
            CampaignFoundationTests.Request(),
            new SplitMix64RandomSource(42),
            new CampaignFoundationTests.FixedClock());

        using var session = host.AttachCampaign(
            campaign, campaign, TestContext.Current.CancellationToken);
        session.Execute(new AdvanceCampaignTime(1));
        var state = host.CaptureState();
        var encoded = ExtensionStateJsonCodec.Write(state);
        var decoded = ExtensionStateJsonCodec.Read(encoded);

        var extension = Assert.Single(host.Extensions);
        Assert.Equal("example.probe", extension.Identity.Id);
        Assert.True(extension.IsEnabled);
        var record = Assert.Single(decoded.Records);
        Assert.Equal(1L, record.Data.Properties!["events"].Scalar);
        Assert.Equal(1L, record.Data.Properties["attached"].Scalar);
        Assert.Equal(1L, record.Data.Properties["initialized"].Scalar);
        Assert.Contains(diagnostics.Snapshot(), diagnostic => diagnostic.Code == "TESTEXT001");
    }

    [Fact]
    public void ThrowingCallbackIsContainedAndDisablesExtension()
    {
        using var installation = new ExtensionInstallation();
        installation.Add("example.throwing", typeof(ThrowOnEventExtension));
        var diagnostics = new DiagnosticCollector();
        using var host = ManagedExtensionHost.LoadFromDirectory(
            installation.Root, diagnostics, cancellationToken: TestContext.Current.CancellationToken);
        var campaign = CampaignFactory.Create(
            CampaignFoundationTests.LoadFixture(),
            CampaignFoundationTests.Request(),
            new SplitMix64RandomSource(42),
            new CampaignFoundationTests.FixedClock());
        using var session = host.AttachCampaign(
            campaign, campaign, TestContext.Current.CancellationToken);

        var result = session.Execute(new AdvanceCampaignTime(1));

        Assert.Single(result.Events);
        Assert.False(Assert.Single(host.Extensions).IsEnabled);
        Assert.Contains(diagnostics.Snapshot(), diagnostic => diagnostic.Code == "EXT1007");
    }

    [Fact]
    public void IncompatibleAndMalformedManifestsReportBoundedFailures()
    {
        using var installation = new ExtensionInstallation();
        installation.Add("example.future", typeof(ProbeExtension), minimumApi: "1.0", maximumApi: "2.0");
        installation.Add("example.legacy", typeof(ProbeExtension), minimumApi: "0.1", maximumApi: "0.2");
        var malformed = Directory.CreateDirectory(Path.Combine(installation.Root, "malformed"));
        File.WriteAllText(Path.Combine(malformed.FullName, "extension.json"), "{}", Encoding.UTF8);
        var diagnostics = new DiagnosticCollector();

        using var host = ManagedExtensionHost.LoadFromDirectory(
            installation.Root, diagnostics, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(host.Extensions);
        Assert.Equal(3, diagnostics.Snapshot().Count(diagnostic => diagnostic.Code == "EXT1001"));
    }

    [Fact]
    public void DuplicateExtensionIdLoadsOnlyTheFirstManifest()
    {
        using var installation = new ExtensionInstallation();
        installation.Add("example.duplicate", typeof(ProbeExtension), directoryName: "first");
        installation.Add("example.duplicate", typeof(ProbeExtension), directoryName: "second");
        var diagnostics = new DiagnosticCollector();

        using var host = ManagedExtensionHost.LoadFromDirectory(
            installation.Root, diagnostics, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(host.Extensions);
        Assert.Contains(diagnostics.Snapshot(), diagnostic =>
            diagnostic.Code == "EXT1001" && diagnostic.Message.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void RequiredMissingStateFailsWhileOptionalStateIsPreserved()
    {
        using var installation = new ExtensionInstallation();
        var diagnostics = new DiagnosticCollector();
        using var host = ManagedExtensionHost.LoadFromDirectory(
            installation.Root, diagnostics, cancellationToken: TestContext.Current.CancellationToken);
        var required = StateRecord("missing.required", required: true);
        var optional = StateRecord("missing.optional", required: false);

        var success = host.RestoreState(
            new ExtensionStateDocument([required, optional]), TestContext.Current.CancellationToken);
        var captured = host.CaptureState();

        Assert.False(success);
        Assert.Equal(2, captured.Records.Count);
        Assert.Contains(diagnostics.Snapshot(), diagnostic => diagnostic.Code == "EXT1010");
        Assert.Contains(diagnostics.Snapshot(), diagnostic => diagnostic.Code == "EXT0010");
    }

    [Fact]
    public void ExtensionStateCodecRejectsDepthAndUnknownProperties()
    {
        var value = ExtensionStateValue.List([ExtensionStateValue.List([ExtensionStateValue.WholeNumber(1)])]);
        var document = new ExtensionStateDocument([
            new ExtensionStateRecord("example.state", new Version(1, 0), 1, false, value),
        ]);

        Assert.Throws<InvalidDataException>(() => ExtensionStateJsonCodec.Write(
            document, new ExtensionStateLimits { MaximumDepth = 2 }));
        Assert.Throws<InvalidDataException>(() => ExtensionStateJsonCodec.Read(
            "{\"schemaVersion\":1,\"extensions\":[],\"unexpected\":true}"u8));
    }

    private static ExtensionStateRecord StateRecord(string id, bool required) => new(
        id,
        new Version(1, 0),
        1,
        required,
        ExtensionStateValue.Map(new Dictionary<string, ExtensionStateValue>
        {
            ["value"] = ExtensionStateValue.WholeNumber(1),
        }));

    private sealed class ExtensionInstallation : IDisposable
    {
        public ExtensionInstallation()
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-extension-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Add(
            string id,
            Type entryType,
            string minimumApi = "0.2",
            string maximumApi = "0.3",
            string? directoryName = null)
        {
            var directory = Directory.CreateDirectory(Path.Combine(Root, directoryName ?? id));
            var sourceAssembly = entryType.Assembly.Location;
            var destinationAssembly = Path.Combine(directory.FullName, Path.GetFileName(sourceAssembly));
            File.Copy(sourceAssembly, destinationAssembly);
            File.WriteAllText(
                Path.Combine(directory.FullName, "extension.json"),
                $$"""
                {
                  "schemaVersion": 1,
                  "id": "{{id}}",
                  "version": "1.0.0",
                  "entryAssembly": "{{Path.GetFileName(sourceAssembly)}}",
                  "entryType": "{{entryType.FullName}}",
                  "minimumApiVersion": "{{minimumApi}}",
                  "maximumApiVersionExclusive": "{{maximumApi}}"
                }
                """,
                Encoding.UTF8);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
