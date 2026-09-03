using Oxce.Extensions.Abstractions;

namespace Oxce.TestExtension;

public sealed class ProbeExtension : IManagedExtension, ICampaignExtension, IManagedExtensionState
{
    private int _eventCount;
    private int _initialized;
    private int _attached;
    private int _restored;

    public void Initialize(IExtensionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.ApiVersion != ManagedExtensionApi.Current)
            throw new InvalidOperationException("Unexpected managed-extension API version.");
        _initialized = 1;
        context.Diagnostics.Report(new ExtensionDiagnostic(
            "TESTEXT001", ExtensionDiagnosticSeverity.Information, "Probe initialized."));
    }

    public void Shutdown(CancellationToken cancellationToken) =>
        cancellationToken.ThrowIfCancellationRequested();

    public void Attach(IExtensionCampaignAccess campaign, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = campaign.QueryOverview();
        _ = campaign.Execute(new ExtensionAdvanceCampaignTime(1));
        _attached = 1;
    }

    public void OnEvent(ExtensionCampaignEvent campaignEvent)
    {
        ArgumentNullException.ThrowIfNull(campaignEvent);
        _eventCount++;
    }

    public void Detach(CancellationToken cancellationToken) =>
        cancellationToken.ThrowIfCancellationRequested();

    public ExtensionStateSnapshot CaptureState() => new(
        1,
        false,
        ExtensionStateValue.Map(new Dictionary<string, ExtensionStateValue>(StringComparer.Ordinal)
        {
            ["attached"] = ExtensionStateValue.WholeNumber(_attached),
            ["events"] = ExtensionStateValue.WholeNumber(_eventCount),
            ["initialized"] = ExtensionStateValue.WholeNumber(_initialized),
            ["restored"] = ExtensionStateValue.WholeNumber(_restored),
        }));

    public void RestoreState(ExtensionStateSnapshot state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (state.SchemaVersion != 1 || state.Data.Kind != ExtensionStateValueKind.Map)
            throw new InvalidDataException("Unsupported probe state.");
        _eventCount = checked((int)(long)state.Data.Properties!["events"].Scalar!);
        _restored = 1;
    }
}

public sealed class ThrowOnEventExtension : IManagedExtension, ICampaignExtension
{
    public void Initialize(IExtensionContext context, CancellationToken cancellationToken)
    {
    }

    public void Shutdown(CancellationToken cancellationToken)
    {
    }

    public void Attach(IExtensionCampaignAccess campaign, CancellationToken cancellationToken)
    {
    }

    public void OnEvent(ExtensionCampaignEvent campaignEvent) =>
        throw new InvalidOperationException("Expected test callback failure.");

    public void Detach(CancellationToken cancellationToken)
    {
    }
}
