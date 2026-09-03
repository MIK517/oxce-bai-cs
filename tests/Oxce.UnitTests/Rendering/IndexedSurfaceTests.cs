using Oxce.Rendering;
using Xunit;

namespace Oxce.UnitTests.Rendering;

public sealed class IndexedSurfaceTests
{
    [Fact]
    public void ConstructorExposesRequestedDimensionsAndPixelCount()
    {
        var surface = new IndexedSurface(3, 2);

        Assert.Equal(3, surface.Width);
        Assert.Equal(2, surface.Height);
        Assert.Equal(6, surface.Pixels.Length);
        Assert.Equal(new byte[6], surface.Pixels.ToArray());
    }

    [Fact]
    public void GetRowAddressesTheUnderlyingPixelBuffer()
    {
        var surface = new IndexedSurface(2, 2);

        surface.GetRow(1)[1] = 42;

        Assert.Equal(42, surface.Pixels[3]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void ConstructorRejectsNonPositiveDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndexedSurface(width, height));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void GetRowRejectsCoordinatesOutsideTheSurface(int y)
    {
        var surface = new IndexedSurface(2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => surface.GetRow(y));
    }

    [Fact]
    public void FillRectangleAndLineClipToSurface()
    {
        var surface = new IndexedSurface(5, 4);

        surface.FillRectangle(-1, 1, 4, 2, 3);
        surface.DrawLine(-1, 3, 5, 3, 7);

        Assert.Equal(new byte[]
        {
            0, 0, 0, 0, 0,
            3, 3, 3, 0, 0,
            3, 3, 3, 0, 0,
            7, 7, 7, 7, 7,
        }, surface.Pixels.ToArray());
    }

    [Fact]
    public void BlitClipsAndPreservesTransparentPixels()
    {
        var source = new IndexedSurface(3, 2);
        new byte[] { 1, 0, 2, 3, 4, 5 }.CopyTo(source.Pixels);
        var target = new IndexedSurface(3, 2);
        target.Clear(9);

        target.Blit(source, 1, 0);

        Assert.Equal(new byte[] { 9, 1, 9, 9, 3, 4 }, target.Pixels.ToArray());
    }

    [Fact]
    public void CropCopiesIntersectionAndLeavesOutsideTransparent()
    {
        var source = new IndexedSurface(2, 2);
        new byte[] { 1, 2, 3, 4 }.CopyTo(source.Pixels);

        var crop = source.Crop(1, 1, 2, 2);

        Assert.Equal(new byte[] { 4, 0, 0, 0 }, crop.Pixels.ToArray());
    }

    [Fact]
    public void ShadedBlitKeepsTransparencyAndColorGroups()
    {
        var source = new IndexedSurface(4, 1);
        new byte[] { 0, 0x21, 0x2f, 0x31 }.CopyTo(source.Pixels);
        var target = new IndexedSurface(4, 1);
        target.Clear(8);

        target.BlitShaded(source, 0, 0, shade: 2);

        Assert.Equal(new byte[] { 8, 0x23, 0x0f, 0x33 }, target.Pixels.ToArray());
    }

    [Fact]
    public void ColorTransformsRetainTransparentIndex()
    {
        var surface = new IndexedSurface(4, 1);
        new byte[] { 0, 0x21, 0x25, 0x2f }.CopyTo(surface.Pixels);

        surface.OffsetColorBlocks(2, 16);
        surface.InvertColors(0x28);

        Assert.Equal(new byte[] { 0, 0x2d, 0x29, 0x20 }, surface.Pixels.ToArray());
    }

    [Fact]
    public void FilledCircleAndPolygonDrawClippedSoftwarePrimitives()
    {
        var surface = new IndexedSurface(5, 5);

        surface.FillCircle(0, 0, 2, 3);
        surface.FillPolygon(
            [new IndexedPoint(2, 2), new IndexedPoint(4, 2), new IndexedPoint(4, 4), new IndexedPoint(2, 4)],
            7);

        Assert.Equal(3, surface.GetPixel(1, 1));
        Assert.Equal(0, surface.GetPixel(2, 1));
        Assert.Equal(7, surface.GetPixel(3, 3));
        Assert.Equal(0, surface.GetPixel(1, 4));
    }

    [Fact]
    public void PolygonFillCanUseCallerOwnedScratchWithoutAllocating()
    {
        var surface = new IndexedSurface(16, 16);
        IndexedPoint[] points =
        [
            new IndexedPoint(1, 1), new IndexedPoint(14, 2),
            new IndexedPoint(12, 14), new IndexedPoint(2, 13),
        ];
        var scratch = new double[points.Length];
        surface.FillPolygon(points, 7, scratch);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1_000; index++) surface.FillPolygon(points, 7, scratch);
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(allocatedBefore, allocatedAfter);
        Assert.Throws<ArgumentException>(() => surface.FillPolygon(points, 7, new double[3]));
    }
}
