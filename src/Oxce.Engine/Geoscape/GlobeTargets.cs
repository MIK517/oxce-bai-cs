using Oxce.Gameplay.Campaigns.World;

namespace Oxce.Engine.Geoscape;

/// <summary>A selectable globe marker. Craft entries also carry the owner and craft identity.</summary>
public sealed record GlobeTarget(WorldTargetReference Reference, string Label, double Longitude, double Latitude)
{
    public WorldTargetKind Kind => Reference.Kind;
    public CampaignWorldCraft? Craft { get; init; }
    public CampaignWorldTarget? Target { get; init; }
    public CampaignWorldBase? Base { get; init; }
}

/// <summary>
/// The selectable globe targets in Globe::getTargets order: each placed base (except one at
/// exactly (0, 0)) followed by its airborne craft, then detected UFOs, waypoints, mission sites and discovered alien bases.
/// Visibility follows the world query, which exposes only detected sites; the reference also
/// lets an undetected site be clicked, which would reveal hidden activity.
/// </summary>
public sealed class GlobeTargets
{
    private readonly GlobeTarget[] _targets;

    private GlobeTargets(GlobeTarget[] targets) => _targets = targets;

    public IReadOnlyList<GlobeTarget> All => _targets;

    public static GlobeTargets Empty { get; } = new([]);

    /// <param name="localize">Names an unnamed craft as Craft::getName does (type and id); identity when absent.</param>
    public static GlobeTargets Build(CampaignWorldOverview world, Func<string, string>? localize = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        localize ??= static text => text;
        var targets = new List<GlobeTarget>();
        foreach (var owner in world.Bases)
        {
            // Globe::getTargets skips a base at exactly (0, 0) together with its craft.
            if (owner.Longitude == 0.0 && owner.Latitude == 0.0) continue;
            targets.Add(new GlobeTarget(new WorldTargetReference(WorldTargetKind.Base, WorldTargetReference.BaseType,
                owner.Id, owner.Longitude, owner.Latitude), owner.Name, owner.Longitude, owner.Latitude)
            {
                Base = owner,
            });
            foreach (var craft in world.Crafts)
            {
                if (craft.BaseId != owner.Id) continue;
                // Grounded craft share the base position and have no destination.
                if (craft.Longitude == owner.Longitude && craft.Latitude == owner.Latitude && craft.Destination is null)
                    continue;
                targets.Add(new GlobeTarget(new WorldTargetReference(WorldTargetKind.Craft, craft.TypeId, craft.Id,
                    craft.Longitude, craft.Latitude), GeoscapeScreens.CraftName(craft, localize), craft.Longitude, craft.Latitude)
                {
                    Craft = craft,
                });
            }
        }
        foreach (var kind in (ReadOnlySpan<WorldTargetKind>)[WorldTargetKind.Ufo, WorldTargetKind.Waypoint,
                     WorldTargetKind.MissionSite, WorldTargetKind.AlienBase])
        {
            foreach (var target in world.Targets)
            {
                if (target.Kind != kind || !target.Detected) continue;
                targets.Add(new GlobeTarget(target.Reference, GeoscapeScreens.TargetLabel(target, localize), target.Longitude, target.Latitude)
                {
                    Target = target,
                });
            }
        }
        return new([.. targets]);
    }

    /// <summary>Globe::getTargets: every target near a frame point, optionally excluding one craft.</summary>
    public IReadOnlyList<GlobeTarget> Near(GlobeView view, int x, int y, WorldTargetReference? excludedCraft = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        List<GlobeTarget>? near = null;
        foreach (var target in _targets)
        {
            if (excludedCraft is not null && target.Kind == WorldTargetKind.Craft &&
                SameCraft(target.Reference, excludedCraft))
                continue;
            if (view.IsNear(target.Longitude, target.Latitude, x, y)) (near ??= []).Add(target);
        }
        return near is null ? [] : near;
    }

    /// <summary>Front-facing targets in selection order, for keyboard navigation.</summary>
    public IReadOnlyList<GlobeTarget> Visible(GlobeView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return _targets.Where(target => !view.IsBack(target.Longitude, target.Latitude)).ToArray();
    }

    /// <summary>Finds a target again after a refresh by kind, type and identity (UFOs by unique id).</summary>
    public GlobeTarget? Find(WorldTargetReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        foreach (var target in _targets)
            if (Same(target.Reference, reference)) return target;
        return null;
    }

    public static bool Same(WorldTargetReference left, WorldTargetReference right) =>
        left.Kind == right.Kind && (left.Kind == WorldTargetKind.Ufo && left.UniqueId > 0 && right.UniqueId > 0
            ? left.UniqueId == right.UniqueId
            : left.Id == right.Id && string.Equals(left.TypeId, right.TypeId, StringComparison.Ordinal));

    private static bool SameCraft(WorldTargetReference left, WorldTargetReference right) =>
        right.Kind == WorldTargetKind.Craft && left.Id == right.Id &&
        string.Equals(left.TypeId, right.TypeId, StringComparison.Ordinal);
}
