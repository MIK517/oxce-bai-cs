using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Engine.Geoscape;

/// <summary>A marker frame at a globe position.</summary>
public readonly record struct GlobeMarker(double Longitude, double Latitude, int Frame);

/// <summary>A radar circle: centre, range in radians, segment count and the drawn-segment stride.</summary>
public readonly record struct GlobeCircle(double Longitude, double Latitude, double Range, int Segments, int Stride = 1);

/// <summary>A great-circle flight path from a mover to its destination.</summary>
public readonly record struct GlobePath(double FromLongitude, double FromLatitude, double ToLongitude, double ToLatitude);

/// <summary>
/// A detail label centred in a reference text box of <paramref name="BoxWidth"/> pixels at the
/// projected point, shown from <paramref name="MinimumZoom"/>; cities also draw their marker.
/// </summary>
public sealed record GlobeLabel(double Longitude, double Latitude, string Text, byte Color, int MinimumZoom,
    int BoxWidth, int OffsetY, bool City = false);

/// <summary>Display options the reference keeps in Options (globeSeasons, globeDetail, ...).</summary>
public sealed record GlobeRenderOptions(bool Seasons = false, bool Detail = true, bool RadarLines = true,
    bool FlightPaths = true);

/// <summary>
/// What the globe draws for one world state, built when the world changes rather than per frame.
/// Marker frames follow each Target::getMarker; order follows Globe::drawMarkers.
/// </summary>
public sealed class GlobeScene
{
    public const int BaseMarker = 0;
    public const int CraftMarker = 1;
    public const int UfoMarker = 2;
    public const int LandedUfoMarker = 3;
    public const int CrashedUfoMarker = 4;
    public const int SiteMarker = 5;
    public const int WaypointMarker = 6;
    public const int CityMarker = 8;

    private GlobeScene(GlobeMarker[] markers, GlobeCircle[] radars, GlobePath[] flights, GlobeLabel[] labels)
    {
        Markers = markers;
        Radars = radars;
        Flights = flights;
        Labels = labels;
    }

    public IReadOnlyList<GlobeMarker> Markers { get; }
    public IReadOnlyList<GlobeCircle> Radars { get; }
    public IReadOnlyList<GlobePath> Flights { get; }
    public IReadOnlyList<GlobeLabel> Labels { get; }

    public static GlobeScene Empty { get; } = new([], [], [], []);

    /// <summary>A scene of explicit parts, for tools and tests.</summary>
    public static GlobeScene FromParts(IEnumerable<GlobeMarker> markers, IEnumerable<GlobeCircle>? radars = null,
        IEnumerable<GlobePath>? flights = null, IEnumerable<GlobeLabel>? labels = null) =>
        new([.. markers], [.. radars ?? []], [.. flights ?? []], [.. labels ?? []]);

    /// <summary>A scene of explicit markers, for tools and tests.</summary>
    public static GlobeScene FromMarkers(IEnumerable<GlobeMarker> markers) => FromParts(markers);

    public static GlobeScene Build(CampaignWorldOverview world, RuntimeRuleCatalog rules,
        Func<string, string>? localize = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rules);
        localize ??= static text => text;
        var markers = new List<GlobeMarker>();
        foreach (var owner in world.Bases)
            // Base::getMarker hides a base at exactly (0, 0).
            if (owner.Longitude != 0.0 || owner.Latitude != 0.0)
                markers.Add(new(owner.Longitude, owner.Latitude, BaseMarker));
        Add(WorldTargetKind.Waypoint);
        Add(WorldTargetKind.MissionSite);
        Add(WorldTargetKind.AlienBase);
        Add(WorldTargetKind.Ufo);
        foreach (var craft in world.Crafts)
        {
            if (craft.Status != "STR_OUT") continue;
            var frame = rules.Crafts.TryGet(craft.TypeId, out var handle) ? rules.Crafts[handle].Value.Marker : -1;
            markers.Add(new(craft.Longitude, craft.Latitude, frame == -1 ? CraftMarker : frame));
        }
        return new([.. markers], Radars(), Flights(), Labels());

        // Globe::drawRadars: base radars (48 segments) each followed by its airborne craft (24),
        // then hunter-killer UFOs and discovered alien bases when the mod draws enemy radars.
        GlobeCircle[] Radars()
        {
            var circles = new List<GlobeCircle>();
            foreach (var owner in world.Bases)
            {
                if ((owner.Longitude != 0.0 || owner.Latitude != 0.0) && owner.RadarRange > 0)
                    circles.Add(new(owner.Longitude, owner.Latitude, Nautical(owner.RadarRange), 48));
                foreach (var craft in world.Crafts)
                    if (craft.BaseId == owner.Id && craft.Status == "STR_OUT" && craft.RadarRange > 0)
                        circles.Add(new(craft.Longitude, craft.Latitude, Nautical(craft.RadarRange), 24));
            }
            var enemy = rules.Campaign.DrawEnemyRadarCircles;
            if (enemy > 0)
            {
                foreach (var ufo in world.Targets)
                    if (ufo.Kind == WorldTargetKind.Ufo && ufo.HunterKiller && (enemy != 1 || ufo.HyperDetected) &&
                        ufo.RadarRange > 0)
                        circles.Add(new(ufo.Longitude, ufo.Latitude, Nautical(ufo.RadarRange), 24));
                foreach (var alienBase in world.Targets)
                    if (alienBase.Kind == WorldTargetKind.AlienBase &&
                        Deployment(alienBase.TypeId) is { BaseDetectionRange: > 0 } deployment)
                        circles.Add(new(alienBase.Longitude, alienBase.Latitude, Nautical(deployment.BaseDetectionRange), 24));
            }
            return [.. circles];
        }

        // Globe::drawFlights: airborne craft with a destination, then hunting UFOs.
        GlobePath[] Flights()
        {
            var paths = new List<GlobePath>();
            foreach (var craft in world.Crafts)
                if (craft.Status == "STR_OUT" && craft.Destination is { } destination)
                    paths.Add(new(craft.Longitude, craft.Latitude, destination.Longitude, destination.Latitude));
            foreach (var ufo in world.Targets)
                if (ufo.Kind == WorldTargetKind.Ufo && ufo.Hunting && ufo.Destination is { } destination)
                    paths.Add(new(ufo.Longitude, ufo.Latitude, destination.Longitude, destination.Latitude));
            return [.. paths];
        }

        // Globe::drawDetail labels: countries from zoom 2, extra labels from their own zoom, then
        // cities (named point mission areas, RuleRegion::getCities) and base names from zoom 3.
        GlobeLabel[] Labels()
        {
            var colors = rules.Campaign.Globe.Colors;
            var labels = new List<GlobeLabel>();
            foreach (var country in rules.Countries.Rules)
                labels.Add(new(country.Value.LabelLongitude, country.Value.LabelLatitude, localize(country.Id),
                    country.Value.LabelColor > 0 ? (byte)country.Value.LabelColor : colors.CountryLabel, 2, 150, 0));
            foreach (var label in rules.Campaign.GlobeLabels)
                labels.Add(new(label.LabelLongitude, label.LabelLatitude, localize(label.Type),
                    label.LabelColor > 0 ? (byte)label.LabelColor : colors.CountryLabel, label.ZoomLevel, 120, 0));
            foreach (var region in rules.Regions.Rules)
                foreach (var zone in region.Value.MissionZones)
                    foreach (var area in zone.Areas)
                        if (area.Name.Length != 0 && Same(area.LongitudeMinimum, area.LongitudeMaximum) &&
                            Same(area.LatitudeMinimum, area.LatitudeMaximum))
                            labels.Add(new(area.LongitudeMinimum, area.LatitudeMinimum, localize(area.Name),
                                colors.CityLabel, 3, 100, 2, City: true));
            foreach (var owner in world.Bases)
                if (owner.Longitude != 0.0 || owner.Latitude != 0.0)
                    labels.Add(new(owner.Longitude, owner.Latitude, owner.Name, colors.BaseLabel, 3, 100, 2));
            return [.. labels];
        }

        void Add(WorldTargetKind kind)
        {
            foreach (var target in world.Targets)
            {
                if (target.Kind != kind || !target.Detected) continue;
                var frame = kind switch
                {
                    WorldTargetKind.Waypoint => WaypointMarker,
                    WorldTargetKind.MissionSite => Deployment(target.TypeId) is { MarkerIcon: not -1 } site
                        ? site.MarkerIcon : SiteMarker,
                    // AlienBase::getMarker has no default: a deployment without an icon draws nothing.
                    WorldTargetKind.AlienBase => Deployment(target.TypeId)?.MarkerIcon ?? -1,
                    _ => UfoFrame(target),
                };
                if (frame != -1) markers.Add(new(target.Longitude, target.Latitude, frame));
            }
        }

        static double Nautical(int miles) => miles * (1 / 60.0) * (Math.PI / 180.0);

        // fmath.h AreSame(double): within DBL_EPSILON scaled by the larger magnitude (at least 1).
        static bool Same(double left, double right) =>
            Math.Abs(left - right) <= 2.220446049250313e-16 * Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));

        RuntimeAlienDeploymentRule? Deployment(string id) =>
            rules.AlienDeployments.TryGet(id, out var handle) ? rules.AlienDeployments[handle].Value : null;

        int UfoFrame(CampaignWorldTarget ufo)
        {
            var rule = rules.Ufos.TryGet(ufo.TypeId, out var handle) ? rules.Ufos[handle].Value : null;
            return ufo.Status switch
            {
                nameof(UfoStatus.Landed) => rule is { LandedMarker: not -1 } ? rule.LandedMarker : LandedUfoMarker,
                nameof(UfoStatus.Crashed) => rule is { CrashedMarker: not -1 } ? rule.CrashedMarker : CrashedUfoMarker,
                _ => rule is { Marker: not -1 } ? rule.Marker : UfoMarker,
            };
        }
    }
}
