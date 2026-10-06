using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Bootstrap;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.Content;

namespace Oxce.Benchmarks;

/// <summary>
/// Builds strategic campaigns from the public <c>strategic-logistics</c> fixture mod and its
/// <c>fixtures/public/savegames</c> overlays, the same inputs the compatibility tests use.
/// </summary>
internal static class PublicCampaignFixtures
{
    private const string ModId = "logistics";

    public static RuntimeContent LoadContent(IReadOnlyList<string> overlays, string? extraRules = null)
    {
        var repository = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), $"oxce-benchmark-content-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(Path.Combine(repository, "fixtures", "public", "mods", "strategic-logistics"),
                Path.Combine(root, "standard"));
            Directory.CreateDirectory(Path.Combine(root, "user", "mods"));
            var rulesets = Path.Combine(root, "standard", ModId, "Ruleset");
            foreach (var overlay in overlays)
                File.Copy(Path.Combine(repository, "fixtures", "public", "savegames", overlay),
                    Path.Combine(rulesets, $"aa-{overlay.Replace("strategic-", "", StringComparison.Ordinal)}"));
            if (extraRules is not null) File.WriteAllText(Path.Combine(rulesets, "zz-benchmark.rul"), extraRules);
            var loaded = InstallationContentLoader.Load(InstallationLoadRequest.ForMasterAndAddOn(
                root, ModId, "-", new ModEngineIdentity("Extended", "8.6.1.0")));
            if (!loaded.IsSuccess) throw new InvalidOperationException(loaded.DescribeFailure());
            return loaded.Content!;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A veteran campaign with its starting base placed at (0.2, 0.1) and no world actors.</summary>
    public static CampaignSnapshot PlacedCampaign(RuntimeContent content, CampaignTime time)
    {
        var campaign = CampaignFactory.Create(content,
            new NewCampaignRequest(new CampaignId(Guid.Parse("5f0c1d2e-3a4b-4c5d-8e9f-a0b1c2d3e4f5")),
                "Benchmark", ModId, [ModId], CampaignDifficulty.Veteran),
            new SplitMix64RandomSource(42), new FixedClock());
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0.2, 0.1));
        return campaign.Capture() with { MonthsPassed = 0, Time = time };
    }

    public static AlienMissionSnapshot Mission(int id, string ruleId, int liveUfos) =>
        // A distant next wave keeps the population fixed during the measured interval.
        new(id, ruleId, "REGION", "RACE_A", 2, 0, 100_000, liveUfos, -1);

    public static UfoSnapshot FlyingUfo(int uniqueId, int id, string ruleId, int missionId, double longitude,
        double latitude, int speed)
    {
        var destination = new WorldTargetReference(WorldTargetKind.Waypoint, WorldTargetReference.WaypointType, 0, 3.0, -0.6);
        return new UfoSnapshot(uniqueId, ruleId, missionId, "TRAJ_PATROL", 0, longitude, latitude, UfoStatus.Flying, "STR_HIGH_UC")
        {
            Id = id,
            Speed = speed,
            MissionWaveNumber = 1,
            Destination = destination,
        };
    }

    public static CampaignSnapshot WithNextId(CampaignSnapshot snapshot, string key, int value)
    {
        var ids = snapshot.NextIds.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        ids[key] = value;
        return snapshot with { NextIds = ids };
    }

    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private sealed class FixedClock : ICampaignClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 2, 20, 0, 0, TimeSpan.Zero);
    }
}
