using Oxce.Formats.Binary;
using Oxce.Formats.Terrain;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateTerrainDataCorpusTests
{
    [Fact]
    public void OwnedGameAndModMcdAndLoftempsFilesParseWithinBounds()
    {
        var privateMods = TestFixtures.RequirePrivateMods();
        var data = TestFixtures.RequireOwnedData();

        var mcdPaths = TestFixtures.CorpusFiles("*.MCD", data, privateMods);
        var loftempsPaths = TestFixtures.CorpusFiles("LOFTEMPS.DAT", data, privateMods);
        Assert.True(mcdPaths.Length > 1_000, $"Expected the supplied MCD corpus; found {mcdPaths.Length} files.");
        Assert.True(loftempsPaths.Length >= 5, $"Expected the supplied LOFTEMPS corpus; found {loftempsPaths.Length} files.");

        TestFixtures.ForEachCorpusFile(mcdPaths, path =>
        {
            var dataSet = McdTerrainCodec.Decode(BinaryDataReader.FromFile(path));
            Assert.NotEmpty(dataSet.Records);
            Assert.InRange(dataSet.TrailingData.Length, 0, McdTerrainCodec.RecordSize - 1);
        });

        TestFixtures.ForEachCorpusFile(loftempsPaths, path =>
        {
            var voxelData = LoftempsCodec.Decode(BinaryDataReader.FromFile(path));
            Assert.NotEmpty(voxelData.Values);
            Assert.InRange(voxelData.TrailingData.Length, 0, sizeof(ushort) - 1);
        });
    }
}
