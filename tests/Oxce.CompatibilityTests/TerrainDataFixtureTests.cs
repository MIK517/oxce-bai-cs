using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Binary;
using Oxce.Formats.Terrain;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class TerrainDataFixtureTests
{
    [Fact]
    public void McdAndLoftempsSemanticsMatchCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "terrain-data.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var mcd = McdTerrainCodec.Decode(ReadHex(root, manifest.Inputs[0].Path));
        var loftemps = LoftempsCodec.Decode(ReadHex(root, manifest.Inputs[1].Path));
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            loftemps = new
            {
                trailing = Convert.ToHexString(loftemps.TrailingData.Span),
                values = loftemps.Values,
            },
            mcd = new
            {
                records = mcd.Records.Select(record => new
                {
                    alternateMcd = record.AlternateMcd,
                    armor = record.Armor,
                    bigWall = record.BigWall,
                    blocksFire = record.BlocksFire,
                    blocksSmoke = record.BlocksSmoke,
                    dieMcd = record.DieMcd,
                    flammable = record.Flammable,
                    footstepSound = record.FootstepSound,
                    frames = Convert.ToHexString(record.Frames.Span),
                    fuel = record.Fuel,
                    hasNoFloor = record.HasNoFloor,
                    highExplosiveBlock = record.HighExplosiveBlock,
                    highExplosiveStrength = record.HighExplosiveStrength,
                    highExplosiveType = record.HighExplosiveType,
                    isDoor = record.IsDoor,
                    isGravLift = record.IsGravLift,
                    isUfoDoor = record.IsUfoDoor,
                    isXcomBase = record.IsXcomBase,
                    lightBlock = record.LightBlock,
                    lightSource = record.LightSource,
                    loftIds = Convert.ToHexString(record.LoftIds.Span),
                    miniMapIndex = record.MiniMapIndex,
                    positionLevel = record.PositionLevel,
                    raw = Convert.ToHexString(record.RawData.Span),
                    smokeBlockage = record.SmokeBlockage,
                    stopsLineOfSight = record.StopsLineOfSight,
                    targetType = record.TargetType,
                    terrainLevel = record.TerrainLevel,
                    tileType = record.TileType,
                    timeUnits = new[]
                    {
                        (int)record.TimeUnitsWalk,
                        record.TimeUnitsFly,
                        record.TimeUnitsSlide,
                    },
                }),
                trailing = Convert.ToHexString(mcd.TrailingData.Span),
            },
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

    private static BinaryDataReader ReadHex(string root, string relativePath) =>
        new(Convert.FromHexString(File.ReadAllText(Path.GetFullPath(relativePath, root)).Trim()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
