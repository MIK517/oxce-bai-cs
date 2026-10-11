using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Engine.Geoscape;

/// <summary>A marker frame at a globe position.</summary>
public readonly record struct GlobeMarker(double Longitude, double Latitude, int Frame);

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

    private GlobeScene(GlobeMarker[] markers) => Markers = markers;

    public IReadOnlyList<GlobeMarker> Markers { get; }

    public static GlobeScene Empty { get; } = new([]);

    /// <summary>A scene of explicit markers, for tools and tests.</summary>
    public static GlobeScene FromMarkers(IEnumerable<GlobeMarker> markers) => new([.. markers]);

    public static GlobeScene Build(CampaignWorldOverview world, RuntimeRuleCatalog rules)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rules);
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
        return new([.. markers]);

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
