using Oxce.Engine.Geoscape;
using Oxce.Rendering;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class GeoscapeAssetsTests
{
    [Fact]
    public void DatFramesAreWholeRowMajorFramesAndIgnoreATrailingPartialFrame()
    {
        var data = new byte[2 * 4 + 3];
        for (var index = 0; index < data.Length; index++) data[index] = (byte)(index + 1);
        var frames = new Dictionary<int, IndexedSurface>();

        GeoscapeAssets.ReadDatFrames(data, 2, 2, frames);

        Assert.Equal([0, 1], frames.Keys.Order());
        Assert.Equal([1, 2, 3, 4], frames[0].Pixels.ToArray());
        Assert.Equal([5, 6, 7, 8], frames[1].Pixels.ToArray());
    }

    [Fact]
    public void SubdividedCellsComeFromTheDeclaredSurfaceAndPadMissingPixels()
    {
        // A 4x2 image declared as 6x2 with 3x2 cells: the second cell has one real column.
        byte[] image = [1, 2, 3, 4, 5, 6, 7, 8];

        var first = GeoscapeAssets.Copy(image, 4, 2, 0, 0, 3, 2, 6, 2);
        var second = GeoscapeAssets.Copy(image, 4, 2, 3, 0, 3, 2, 6, 2);
        var clipped = GeoscapeAssets.Copy(image, 4, 2, 0, 0, 3, 2, 2, 2);

        Assert.Equal([1, 2, 3, 5, 6, 7], first.Pixels.ToArray());
        Assert.Equal([4, 0, 0, 8, 0, 0], second.Pixels.ToArray());
        Assert.Equal([1, 2, 0, 5, 6, 0], clipped.Pixels.ToArray());
    }
}
