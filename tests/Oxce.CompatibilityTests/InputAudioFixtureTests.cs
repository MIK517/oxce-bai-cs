using System.Globalization;
using System.Text.Json;
using Oxce.Engine.Audio;
using Oxce.Engine.Input;
using Oxce.FixtureSupport;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class InputAudioFixtureTests
{
    [Fact]
    public void PointerCoordinatesAndVolumeCurveMatchCapturedCppReference()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("input-audio-semantics");
        var pointerCases = new List<object>();
        var volumes = new List<object>();
        foreach (var line in File.ReadLines(Path.GetFullPath(manifest.Inputs[0].Path, root)))
        {
            var fields = line.Split('\t');
            if (fields[0] == "pointer")
            {
                var mapped = InputCoordinateMapper.Map(
                    ParseDouble(fields[1]),
                    ParseDouble(fields[2]),
                    ParseDouble(fields[3]),
                    ParseDouble(fields[4]),
                    ParseInt32(fields[5]),
                    ParseInt32(fields[6]),
                    ParseInt32(fields[7]),
                    ParseInt32(fields[8]));
                pointerCases.Add(new
                {
                    logicalX = mapped.LogicalX,
                    logicalY = mapped.LogicalY,
                    surfaceX = mapped.SurfaceX,
                    surfaceY = mapped.SurfaceY,
                    windowX = mapped.WindowX,
                    windowY = mapped.WindowY,
                });
            }
            else if (fields[0] == "volume")
            {
                var setting = ParseInt32(fields[1]);
                volumes.Add(new { mixerVolume = AudioVolumeCurve.ToLegacyMixerVolume(setting), setting });
            }
            else
            {
                throw new InvalidDataException($"Unknown input/audio fixture case '{fields[0]}'.");
            }
        }

        var actual = JsonSerializer.SerializeToUtf8Bytes(new { pointers = pointerCases, volumes });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));
        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
    }

    private static double ParseDouble(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int ParseInt32(string value) =>
        int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
