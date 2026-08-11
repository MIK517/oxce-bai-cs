using System.Text.Json;
using System.Globalization;
using Oxce.FixtureSupport;
using Oxce.Formats.Binary;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class BinaryFixtureTests
{
    [Fact]
    public void EndianPrimitivesMatchCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "binary-endian-primitives.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixturePath = Path.GetFullPath(manifest.Inputs[0].Path, root);
        var hex = string.Concat(File.ReadAllText(fixturePath).Where(value => !char.IsWhiteSpace(value)));
        var reader = new BinaryDataReader(Convert.FromHexString(hex));
        var byteValue = reader.ReadByte();
        var signedByte = reader.ReadSByte();
        var uint16Little = reader.ReadUInt16LittleEndian();
        var uint16Big = reader.ReadUInt16BigEndian();
        var uint32Little = reader.ReadUInt32LittleEndian();
        var uint32Big = reader.ReadUInt32BigEndian();
        var singleLittle = reader.ReadSingleLittleEndian();
        var singleBig = reader.ReadSingleBigEndian();
        var uint64Little = reader.ReadUInt64LittleEndian();
        reader.RequireEnd();
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            @byte = byteValue.ToString(CultureInfo.InvariantCulture),
            signedByte = signedByte.ToString(CultureInfo.InvariantCulture),
            singleBig = singleBig.ToString("G9", CultureInfo.InvariantCulture),
            singleLittle = singleLittle.ToString("G9", CultureInfo.InvariantCulture),
            uint16Big = uint16Big.ToString(CultureInfo.InvariantCulture),
            uint16Little = uint16Little.ToString(CultureInfo.InvariantCulture),
            uint32Big = uint32Big.ToString(CultureInfo.InvariantCulture),
            uint32Little = uint32Little.ToString(CultureInfo.InvariantCulture),
            uint64Little = uint64Little.ToString(CultureInfo.InvariantCulture),
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

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
