using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed record CreateCampaignBase(string Name, double Longitude, double Latitude, string LiftRuleId, int X, int Y) : ICampaignCommand;
public sealed record CampaignBaseCreated(int BaseId, string Name, long Cost) : ICampaignEvent;
public sealed record CampaignBaseSite(double Longitude, double Latitude, int Cost, bool FakeUnderwater, string? UnavailableReason);

public sealed partial class CampaignState
{
    public IReadOnlyList<CampaignBaseSite> QueryBaseSites(bool startingBase)
    {
        lock (_transactionGate)
        {
            var sites = new List<CampaignBaseSite>();
            foreach (var polygon in _content.RuntimeRules.Campaign.Globe.Polygons)
            {
                var x = polygon.Points.Sum(p => Math.Cos(p.Latitude) * Math.Cos(p.Longitude));
                var y = polygon.Points.Sum(p => Math.Cos(p.Latitude) * Math.Sin(p.Longitude));
                var z = polygon.Points.Sum(p => Math.Sin(p.Latitude));
                var longitude = (Math.Atan2(y, x) + Math.Tau) % Math.Tau;
                var latitude = Math.Atan2(z, Math.Sqrt(x * x + y * y));
                var site = QueryBaseSite(longitude, latitude, startingBase);
                if (site.UnavailableReason is null) sites.Add(site);
                if (sites.Count == 128) break;
            }
            return sites.AsReadOnly();
        }
    }
    public CampaignBaseSite QueryBaseSite(double longitude, double latitude, bool startingBase = false)
    {
        lock (_transactionGate)
        {
            var texture = StrategicGeography.TextureAt(_content.RuntimeRules.Campaign.Globe, longitude, latitude);
            return new(longitude, latitude, startingBase ? 0 : BaseCost(longitude, latitude),
                texture is { } id && _content.RuntimeRules.Campaign.Globe.Textures.GetValueOrDefault(id)?.FakeUnderwater == true,
                BaseSiteRestriction(longitude, latitude, startingBase));
        }
    }

    private string? BaseSiteRestriction(double longitude, double latitude, bool first)
    {
        if (!double.IsFinite(longitude) || longitude < 0 || longitude >= Math.Tau || !double.IsFinite(latitude) || Math.Abs(latitude) > Math.PI / 2)
            return "Base coordinates are outside their supported range.";
        var settings = _content.RuntimeRules.Campaign;
        var texture = StrategicGeography.TextureAt(settings.Globe, longitude, latitude);
        if (texture is null || settings.Globe.Textures.GetValueOrDefault(texture.Value)?.IsOcean == true)
            return "STR_XCOM_BASE_CANNOT_BE_BUILT";
        if (settings.Globe.Textures.GetValueOrDefault(texture.Value)?.FakeUnderwater == true &&
            (first || settings.FakeUnderwaterBaseUnlockResearch is { } gate && !_completedResearch.Contains(_content.RuntimeRules.Research.GetExternalId(gate))))
            return "STR_XCOM_BASE_CANNOT_BE_BUILT";
        if (!first && settings.NewBaseUnlockResearch is { } research && !_completedResearch.Contains(_content.RuntimeRules.Research.GetExternalId(research)))
            return "Required research is not complete.";
        return null;
    }

    private int BaseCost(double longitude, double latitude)
    {
        foreach (var region in _regions)
        {
            var rule = _content.RuntimeRules.Regions[region.Rule].Value;
            if (rule.Areas.Any(a => AreaContains(a, longitude, latitude))) return rule.BaseCost;
        }
        return 0;
    }

    private CampaignCommandResult CreateBase(CreateCampaignBase command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Name);
        if (_bases.Any(b => !b.IsPlaced)) return Blocked("Place the starting base first.");
        if (_bases.Count >= Options.MaximumBases) return Blocked("Maximum number of bases reached.");
        var site = QueryBaseSite(command.Longitude, command.Latitude);
        if (site.UnavailableReason is { } reason) return Blocked(reason);
        var handle = _content.RuntimeRules.Facilities.GetRequired(command.LiftRuleId);
        var lift = _content.RuntimeRules.Facilities[handle].Value;
        if (!lift.Lift || !FacilityFits(lift, command.X, command.Y) || !FacilityAllowed(lift, site.FakeUnderwater))
            return Blocked("Select a compatible access lift and a position inside the base.");
        if (_funds[^1] < site.Cost) return Blocked("STR_NOT_ENOUGH_MONEY");
        long funds = _funds[^1], income = _incomes[^1], spending = _expenditures[^1];
        Account(-(long)site.Cost, ref funds, ref income, ref spending);
        var id = Math.Max(_nextIds.GetValueOrDefault("oxcePortBase", 1), checked(_bases.Max(b => b.Id) + 1));
        var next = checked(id + 1);
        var state = new BaseState(id, command.Name, command.Longitude, command.Latitude,
            [new(handle, command.X, command.Y, 0, 0, false, false, false)], [], [], new(), 0, 0)
        { FakeUnderwater = site.FakeUnderwater };
        _bases.Add(state);
        _nextIds["oxcePortBase"] = next;
        _funds[^1] = funds;
        _incomes[^1] = income;
        _expenditures[^1] = spending;
        _logisticsQuote = null;
        return new([new CampaignBaseCreated(id, command.Name, site.Cost)]);
    }

    private static bool FacilityFits(RuntimeFacilityRule rule, int x, int y) =>
        x >= 0 && y >= 0 && rule.SizeX > 0 && rule.SizeY > 0 && (long)x + rule.SizeX <= BaseGridSize && (long)y + rule.SizeY <= BaseGridSize;
    private static bool FacilityAllowed(RuntimeFacilityRule rule, bool underwater) => rule.FakeUnderwater == -1 || rule.FakeUnderwater == (underwater ? 1 : 0);
}
