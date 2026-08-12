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
}
