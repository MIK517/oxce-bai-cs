namespace Oxce.Engine.Input;

public readonly record struct MappedPointerPosition(
    double WindowX,
    double WindowY,
    double LogicalX,
    double LogicalY,
    double SurfaceX,
    double SurfaceY);

public static class InputCoordinateMapper
{
    public static MappedPointerPosition Map(
        double windowX,
        double windowY,
        double scaleX,
        double scaleY,
        int leftBlackBand,
        int topBlackBand,
        int surfaceX,
        int surfaceY)
    {
        if (!double.IsFinite(windowX))
        {
            throw new ArgumentOutOfRangeException(nameof(windowX), "Pointer coordinates must be finite.");
        }

        if (!double.IsFinite(windowY))
        {
            throw new ArgumentOutOfRangeException(nameof(windowY), "Pointer coordinates must be finite.");
        }

        if (!double.IsFinite(scaleX) || scaleX <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleX));
        }

        if (!double.IsFinite(scaleY) || scaleY <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleY));
        }

        var contentX = windowX - leftBlackBand;
        var contentY = windowY - topBlackBand;
        return new MappedPointerPosition(
            contentX,
            contentY,
            contentX / scaleX,
            contentY / scaleY,
            contentX - (surfaceX * scaleX),
            contentY - (surfaceY * scaleY));
    }
}
