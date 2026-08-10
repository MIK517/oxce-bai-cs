using Oxce.Core.Geometry;
using Xunit;

namespace Oxce.UnitTests.Core;

public sealed class Position3Tests
{
    [Fact]
    public void TileAndVoxelConversionsUseReferenceDimensions()
    {
        var tile = new Position3(2, -3, 4);

        Assert.Equal(new Position3(32, -48, 96), tile.ToVoxel());
        Assert.Equal(new Position3(-1, 1, -1), new Position3(-17, 31, -25).ToTile());
        Assert.Equal(new Position3(-1, 15, -1), new Position3(-17, 31, -25).ClipVoxel());
    }

    [Fact]
    public void DistanceRulesMatchReferenceRounding()
    {
        Assert.Equal(25, Position3.DistanceSquared(new Position3(1, 2, 3), new Position3(4, 6, 3)));
        Assert.Equal(3, Position3.Distance2D(new Position3(0, 0, 0), new Position3(2, 2, 0)));
    }

    [Fact]
    public void ComponentsUseSignedSixteenBitStorage()
    {
        var position = new Position3(short.MaxValue + 1, short.MinValue - 1, 0);

        Assert.Equal(short.MinValue, position.X);
        Assert.Equal(short.MaxValue, position.Y);
    }
}
