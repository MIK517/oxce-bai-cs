using Oxce.Extensions.Abstractions;
using Oxce.Gameplay.Campaigns;

namespace Oxce.Extensions;

public sealed class CampaignExtensionSession :
    ICampaignQuery,
    ICampaignCommandTarget,
    IExtensionCampaignQueries,
    IExtensionCampaignCommands,
    IDisposable
{
    private readonly ManagedExtensionHost _host;
    private readonly ICampaignQuery _queries;
    private readonly ICampaignCommandTarget _commands;
    private readonly object _gate = new();
    private readonly List<ManagedExtensionHost.ExtensionInstance> _attached = [];
    private bool _dispatching;
    private bool _disposed;

    internal CampaignExtensionSession(
        ManagedExtensionHost host,
        ICampaignQuery queries,
        ICampaignCommandTarget commands,
        CancellationToken cancellationToken)
    {
        _host = host;
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        var capabilities = new ExtensionCampaignCapabilities(this, this);
        foreach (var extension in host.EnabledCampaignExtensions())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ((ICampaignExtension)extension.Instance).Attach(capabilities, cancellationToken);
                _attached.Add(extension);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                host.Disable(extension, "attach to campaign", exception);
            }
        }
    }

    public CampaignOverview QueryOverview()
    {
        ThrowIfDisposed();
        return _queries.QueryOverview();
    }

    ExtensionCampaignOverview IExtensionCampaignQueries.QueryOverview() => Map(QueryOverview());

    public CampaignCommandResult Execute(ICampaignCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_dispatching)
                throw new InvalidOperationException("Campaign commands cannot be submitted from an extension event callback.");
            var result = _commands.Execute(command);
            Dispatch(result.Events.Select(TryMap).OfType<ExtensionCampaignEvent>().ToArray());
            return result;
        }
    }

    ExtensionCampaignCommandResult IExtensionCampaignCommands.Execute(ExtensionCampaignCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = Execute(command switch
        {
            ExtensionAdvanceCampaignTime advance => new AdvanceCampaignTime(advance.FiveSecondTicks),
            ExtensionPlaceStartingBase place => new PlaceStartingBase(
                place.BaseIndex, place.Name, place.Longitude, place.Latitude),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.GetType().FullName, null),
        });
        return new ExtensionCampaignCommandResult(
            result.Events.Select(TryMap).OfType<ExtensionCampaignEvent>().ToArray());
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var extension in _attached.AsEnumerable().Reverse())
            {
                if (!extension.IsEnabled) continue;
                try
                {
                    ((ICampaignExtension)extension.Instance).Detach(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    _host.Disable(extension, "detach from campaign", exception);
                }
            }
            _attached.Clear();
        }
    }

    private void Dispatch(IReadOnlyList<ExtensionCampaignEvent> events)
    {
        _dispatching = true;
        try
        {
            foreach (var campaignEvent in events)
            {
                foreach (var extension in _attached.Where(static candidate => candidate.IsEnabled).ToArray())
                {
                    try
                    {
                        ((ICampaignExtension)extension.Instance).OnEvent(campaignEvent);
                    }
                    catch (Exception exception)
                    {
                        _host.Disable(extension, "observe a campaign event", exception);
                    }
                }
            }
        }
        finally
        {
            _dispatching = false;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static ExtensionCampaignOverview Map(CampaignOverview source) => new(
        source.Id.Value,
        source.Name,
        Map(source.Time),
        source.DaysPassed,
        source.Funds,
        source.CountryCount,
        source.RegionCount,
        source.Bases.Select(static item => new ExtensionCampaignBaseOverview(
            item.Id,
            item.Name,
            item.Longitude,
            item.Latitude,
            item.Facilities.Select(static facility => new ExtensionCampaignFacilityOverview(
                facility.X, facility.Y, facility.SizeX, facility.SizeY, facility.BuildTime)).ToArray(),
            item.CraftCount,
            item.SoldierCount,
            item.ItemTypeCount,
            item.Scientists,
            item.Engineers)).ToArray());

    private static ExtensionCampaignEvent? TryMap(ICampaignEvent source) => source switch
    {
        CampaignTimeAdvanced advanced => new ExtensionCampaignTimeAdvanced(
            Map(advanced.Previous),
            Map(advanced.Current),
            new ExtensionCampaignTimeTriggerSummary(
                advanced.Summary.TickCount,
                advanced.Summary.FiveSeconds,
                advanced.Summary.TenMinutes,
                advanced.Summary.ThirtyMinutes,
                advanced.Summary.OneHour,
                advanced.Summary.OneDay,
                advanced.Summary.OneMonth)),
        StartingBasePlaced placed => new ExtensionStartingBasePlaced(
            placed.BaseIndex, placed.Name, placed.Longitude, placed.Latitude),
        _ => null,
    };

    private static ExtensionCampaignTime Map(CampaignTime source) => new(
        source.Year, source.Month, source.Day, source.Hour, source.Minute, source.Second);
}
