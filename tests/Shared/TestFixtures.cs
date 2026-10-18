using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
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

    /// <summary>Compares an oracle element with actual UTF-8 JSON, both canonicalized, so a failure prints the difference.</summary>
    internal static void AssertJsonEqual(JsonElement expected, ReadOnlySpan<byte> actual) =>
        Assert.Equal(CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(expected.GetRawText())), CanonicalJson.Normalize(actual));

    /// <summary>Compares a value serialized as JSON (with its static type) with actual UTF-8 JSON, both canonicalized.</summary>
    internal static void AssertJsonEqual<T>(T expected, ReadOnlySpan<byte> actual) =>
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));

    /// <summary><c>fixtures/private/mods</c>; skips the test when the private mod corpus is absent.</summary>
    internal static string RequirePrivateMods()
    {
        var path = RepositoryPath("fixtures", "private", "mods");
        Assert.SkipUnless(Directory.Exists(path), "Private mod assets are not available in this checkout.");
        return path;
    }

    /// <summary><c>data</c>, the owned original game files; skips the test when they are absent.</summary>
    internal static string RequireOwnedData()
    {
        var path = RepositoryPath("data");
        Assert.SkipUnless(Directory.Exists(path), "Private original-game assets are not available in this checkout.");
        return path;
    }

    /// <summary>
    /// <c>artifacts/private-install</c>, the installation <c>tools/stage-private-install.ps1</c> stages; skips the
    /// test when it has not been staged.
    /// </summary>
    internal static string RequirePrivateInstallation()
    {
        var path = RepositoryPath("artifacts", "private-install");
        Assert.SkipUnless(File.Exists(Path.Combine(path, ".oxce-private-install-manifest.json")),
            "The private installation is not staged; run tools/stage-private-install.ps1.");
        return path;
    }

    /// <summary>Every file matching the pattern under the roots, in ordinal path order, so failures repeat run to run.</summary>
    internal static string[] CorpusFiles(string pattern, params string[] roots) => roots
        .SelectMany(root => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
        .Order(StringComparer.Ordinal).ToArray();

    /// <summary>Runs the check on every corpus file; a failure names the file that caused it.</summary>
    internal static void ForEachCorpusFile(IEnumerable<string> paths, Action<string> check)
    {
        foreach (var path in paths)
        {
            try { check(path); }
            catch (Exception error) when (error is not Xunit.Sdk.SkipException)
            {
                throw new Xunit.Sdk.XunitException($"Corpus file '{path}' failed: {error.Message}", error);
            }
        }
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
    internal sealed class CountingMinimumRandomSource : IStatefulRandomSource
    {
        internal int IntegerCalls { get; private set; }
        internal int UnitCalls { get; private set; }
        public ulong State { get; private set; }
        public int NextExclusive(int maximum) => NextInclusive(0, maximum - 1);
        public int NextInclusive(int minimum, int maximum)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(minimum, maximum);
            IntegerCalls++;
            State++;
            return minimum;
        }
        public double NextUnit() { UnitCalls++; State++; return 0; }
        public void Restore(ulong state) => State = state;
    }

    internal sealed class RecordingMinimumRandomSource : IStatefulRandomSource
    {
        internal List<(int Minimum, int Maximum)> Calls { get; } = [];
        public ulong State { get; private set; }
        public int NextExclusive(int exclusiveMaximum) => NextInclusive(0, exclusiveMaximum - 1);
        public int NextInclusive(int minimum, int maximum)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(minimum, maximum);
            Calls.Add((minimum, maximum));
            State++;
            return minimum;
        }
        public double NextUnit() => throw new NotSupportedException();
        public void Restore(ulong state) => State = state;
    }

}
