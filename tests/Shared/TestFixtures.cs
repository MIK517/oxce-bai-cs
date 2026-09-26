using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Xunit;

namespace Oxce.TestSupport;

// Linked into both test projects so fixture loading, campaign creation and save options
// stay consistent between unit and compatibility tests.
internal static class TestFixtures
{
    internal static CampaignSnapshot CreateWorldLifecycleSnapshot(RuntimeContent content)
    {
        var campaign = CreateLogisticsCampaign(content, "World lifecycle", CampaignDifficulty.Veteran);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0.2, 0.1));
        var snapshot = campaign.Capture();
        return snapshot with
        {
            MonthsPassed = 0,
            RandomState = 17,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 0, 0),
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(4, "MISSION_LANDING", "REGION", "RACE_A", 1, 0, 1500, 1, -1)],
                Ufos = [new UfoSnapshot(9, "UFO_SCOUT", 4, "TRAJ_LANDING", 2,
                    8 * Math.PI / 180, 2 * Math.PI / 180, UfoStatus.Landed, WorldAltitudes.Ground)
                {
                    Id = 3, MissionWaveNumber = 0, Shield = 0, SecondsRemaining = 10,
                    Destination = WorldTargetReference.ForWaypoint(0, new(8.01 * Math.PI / 180, 2 * Math.PI / 180)),
                }],
            },
        };
    }

    internal const string LogisticsModId = "logistics";

    internal static readonly CampaignId DefaultCampaignId = new(Guid.Parse("0a4c5e6f-7b8d-4e9f-a0b1-c2d3e4f5a6b7"));

    internal static ModEngineIdentity Engine { get; } = new("Extended", "8.6.1.0");

    internal static string RepositoryPath(params string[] segments) =>
        Path.Combine(FixturePaths.FindRepositoryRoot(), Path.Combine(segments));

    internal static string PublicModsPath(string fixture) =>
        RepositoryPath("fixtures", "public", "mods", fixture);

    internal static ModLoadPlan CreatePlan(
        string modsRoot,
        string master = "fixture",
        IReadOnlyList<string>? activeMods = null)
    {
        var discovery = ModDiscovery.ScanDirectory(modsRoot);
        var activations = (activeMods ?? new[] { master })
            .Select(static id => new ModActivation(id, true))
            .ToArray();
        return ModLoadPlanner.Create(ModCatalog.Create(discovery), activations, master, Engine);
    }

    internal static IReadOnlyList<string> RuntimeRuleLinkingMods { get; } = ["runtime-master", "runtime-addon"];

    internal static ModLoadPlan CreateRuntimeRuleLinkingPlan() =>
        CreatePlan(PublicModsPath("runtime-rule-linking"), RuntimeRuleLinkingMods[0], RuntimeRuleLinkingMods);

    internal static RuntimeContent LoadStrategicLogistics()
    {
        var snapshot = ContentSnapshotBuilder.Build(
            CreatePlan(PublicModsPath("strategic-logistics"), LogisticsModId));
        Assert.True(snapshot.Content.Capabilities.Has(ContentLoadStage.RuntimeLinked),
            string.Join(Environment.NewLine, snapshot.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        return snapshot.Content;
    }

    internal static CampaignState CreateLogisticsCampaign(
        RuntimeContent content,
        string name,
        CampaignDifficulty difficulty = CampaignDifficulty.Beginner,
        ulong seed = 42) =>
        CampaignFactory.Create(
            content,
            new NewCampaignRequest(DefaultCampaignId, name, LogisticsModId, [LogisticsModId], difficulty),
            new SplitMix64RandomSource(seed),
            FixedClock.Instance);

    internal static OxceSaveLoadOptions LogisticsSaveOptions() =>
        new(LogisticsModId, new HashSet<string>(StringComparer.Ordinal) { LogisticsModId });

    internal static LoadedOxceCampaign LoadLogisticsSave(
        string yaml,
        RuntimeContent content,
        ulong seed = 1,
        string name = "logistics.sav") =>
        OxceSaveAdapter.Load(yaml, name, content, new SplitMix64RandomSource(seed), LogisticsSaveOptions());

    // Oracle outputs are only read through their manifest, so their pinned inputs are verified first.
    internal static string VerifiedExpectedPath(string manifestId)
    {
        var (root, manifest) = LoadVerifiedManifest(manifestId);
        return Path.GetFullPath(manifest.Expected, root);
    }

    internal static JsonDocument ReadVerifiedExpected(string manifestId) =>
        JsonDocument.Parse(File.ReadAllText(VerifiedExpectedPath(manifestId)));

    internal static (string Root, FixtureManifest Manifest) LoadVerifiedManifest(string name)
    {
        var root = FixturePaths.FindRepositoryRoot();
        var manifest = FixtureManifestLoader.Load(Path.Combine(root, "fixtures", "manifests", name + ".json"));
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        return (root, manifest);
    }

    // Reads the "key=value" line format used by the generated binary fixtures.
    internal static Dictionary<string, string> ReadKeyValues(string path) =>
        File.ReadLines(path)
            .Where(static line => line.Length != 0)
            .Select(static line => line.Split('=', 2))
            .ToDictionary(static values => values[0], static values => values[1], StringComparer.Ordinal);

    // Oracle rows must never be silently empty: an empty array would turn every loop into a no-op.
    internal static JsonElement[] Rows(JsonElement root, string property)
    {
        var rows = root.GetProperty(property).EnumerateArray().ToArray();
        Assert.True(rows.Length > 0, $"Oracle array '{property}' is empty.");
        return rows;
    }

    internal static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    internal sealed class FixedClock : ICampaignClock
    {
        internal static FixedClock Instance { get; } = new();

        public DateTimeOffset UtcNow => new(2026, 9, 2, 20, 0, 0, TimeSpan.Zero);
    }
}
