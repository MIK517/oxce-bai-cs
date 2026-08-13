namespace Oxce.Rendering;

/// <summary>
/// Managed representation of the engine's canonical 8-bit indexed surface.
/// Platform backends convert these indices and a 256-color palette for display.
/// </summary>
public sealed class IndexedSurface
{
    private readonly byte[] _pixels;

    public IndexedSurface(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _pixels = new byte[checked(width * height)];
    }

    public int Width { get; }

    public int Height { get; }

    public Span<byte> Pixels => _pixels;

    public Span<byte> GetRow(int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return _pixels.AsSpan(checked(y * Width), Width);
    }

    public byte GetPixel(int x, int y)
    {
        ValidateCoordinates(x, y);
        return _pixels[checked((y * Width) + x)];
    }

    public void SetPixel(int x, int y, byte color)
    {
        ValidateCoordinates(x, y);
        _pixels[checked((y * Width) + x)] = color;
    }

    public void Clear(byte color = 0) => _pixels.AsSpan().Fill(color);

    public void FillRectangle(int x, int y, int width, int height, byte color)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        var left = Math.Max(x, 0);
        var top = Math.Max(y, 0);
        var right = Math.Min((long)x + width, Width);
        var bottom = Math.Min((long)y + height, Height);
        if (left >= right || top >= bottom)
        {
            return;
        }

        for (var row = top; row < bottom; row++)
        {
            GetRow(row).Slice(left, checked((int)right - left)).Fill(color);
        }
    }

    public void DrawLine(int x1, int y1, int x2, int y2, byte color)
    {
        var deltaX = Math.Abs((long)x2 - x1);
        var stepX = x1 < x2 ? 1 : -1;
        var deltaY = -Math.Abs((long)y2 - y1);
        var stepY = y1 < y2 ? 1 : -1;
        var error = deltaX + deltaY;
        while (true)
        {
            SetPixelIfVisible(x1, y1, color);
            if (x1 == x2 && y1 == y2)
            {
                return;
            }

            var doubledError = error * 2;
            if (doubledError >= deltaY)
            {
                error += deltaY;
                x1 += stepX;
            }

            if (doubledError <= deltaX)
            {
                error += deltaX;
                y1 += stepY;
            }
        }
    }

    public void FillCircle(int centerX, int centerY, int radius, byte color)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        var top = Math.Max((long)centerY - radius, 0);
        var bottom = Math.Min((long)centerY + radius, Height - 1L);
        var radiusSquared = (double)radius * radius;
        for (var y = top; y <= bottom; y++)
        {
            var deltaY = y - centerY;
            var halfWidth = (long)Math.Floor(Math.Sqrt(radiusSquared - ((double)deltaY * deltaY)));
            var left = Math.Max((long)centerX - halfWidth, 0);
            var right = Math.Min((long)centerX + halfWidth, Width - 1L);
            if (left <= right)
            {
                GetRow((int)y).Slice((int)left, checked((int)(right - left + 1))).Fill(color);
            }
        }
    }

    public void FillPolygon(ReadOnlySpan<IndexedPoint> points, byte color)
    {
        if (points.Length < 3)
        {
            throw new ArgumentException("A filled polygon requires at least three points.", nameof(points));
        }

        var minimumY = points[0].Y;
        var maximumY = points[0].Y;
        for (var index = 1; index < points.Length; index++)
        {
            minimumY = Math.Min(minimumY, points[index].Y);
            maximumY = Math.Max(maximumY, points[index].Y);
        }

        var top = Math.Max(minimumY, 0);
        var bottom = Math.Min(maximumY, Height - 1);
        var intersections = new double[points.Length];
        for (var y = top; y <= bottom; y++)
        {
            var intersectionCount = 0;
            for (var index = 0; index < points.Length; index++)
            {
                var first = points[index];
                var second = points[(index + 1) % points.Length];
                if ((first.Y <= y && second.Y > y) || (second.Y <= y && first.Y > y))
                {
                    intersections[intersectionCount++] = first.X
                        + (((double)y - first.Y) * (second.X - first.X) / (second.Y - first.Y));
                }
            }

            Array.Sort(intersections, 0, intersectionCount);
            for (var index = 0; index + 1 < intersectionCount; index += 2)
            {
                var left = Math.Max((long)Math.Ceiling(intersections[index]), 0);
                var right = Math.Min((long)Math.Floor(intersections[index + 1]), Width - 1L);
                if (left <= right)
                {
                    GetRow(y).Slice((int)left, checked((int)(right - left + 1))).Fill(color);
                }
            }
        }
    }

    public void Blit(IndexedSurface source, int destinationX, int destinationY, bool transparent = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        var sourceLeft = Math.Max(0, -destinationX);
        var sourceTop = Math.Max(0, -destinationY);
        var copyWidth = Math.Min(source.Width - sourceLeft, Width - Math.Max(destinationX, 0));
        var copyHeight = Math.Min(source.Height - sourceTop, Height - Math.Max(destinationY, 0));
        if (copyWidth <= 0 || copyHeight <= 0)
        {
            return;
        }

        var targetX = Math.Max(destinationX, 0);
        var targetY = Math.Max(destinationY, 0);
        for (var y = 0; y < copyHeight; y++)
        {
            var sourceRow = source.GetRow(sourceTop + y).Slice(sourceLeft, copyWidth);
            var targetRow = GetRow(targetY + y).Slice(targetX, copyWidth);
            if (!transparent)
            {
                sourceRow.CopyTo(targetRow);
                continue;
            }

            for (var x = 0; x < sourceRow.Length; x++)
            {
                if (sourceRow[x] != 0)
                {
                    targetRow[x] = sourceRow[x];
                }
            }
        }
    }

    public void BlitShaded(
        IndexedSurface source,
        int destinationX,
        int destinationY,
        int shade,
        bool rightHalfOnly = false,
        int replacementColorGroup = -1)
    {
        ArgumentNullException.ThrowIfNull(source);
        BlitShaded(
            source,
            0,
            0,
            source.Width,
            source.Height,
            destinationX,
            destinationY,
            shade,
            rightHalfOnly,
            replacementColorGroup);
    }

    public void BlitShaded(
        IndexedSurface source,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int destinationX,
        int destinationY,
        int shade,
        bool rightHalfOnly = false,
        int replacementColorGroup = -1)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceX);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceY);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceHeight);
        if (sourceX > source.Width - sourceWidth || sourceY > source.Height - sourceHeight)
        {
            throw new ArgumentException("Source rectangle extends beyond the source surface.", nameof(sourceWidth));
        }

        if (replacementColorGroup is < -1 or > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(replacementColorGroup));
        }

        var firstSourceX = rightHalfOnly ? sourceX + (sourceWidth / 2) : sourceX;
        for (var y = sourceY; y < sourceY + sourceHeight; y++)
        {
            var targetY = destinationY + y - sourceY;
            if ((uint)targetY >= (uint)Height)
            {
                continue;
            }

            var sourceRow = source.GetRow(y);
            var targetRow = GetRow(targetY);
            for (var x = firstSourceX; x < sourceX + sourceWidth; x++)
            {
                var targetX = destinationX + x - sourceX;
                var sourcePixel = sourceRow[x];
                if ((uint)targetX >= (uint)Width || sourcePixel == 0)
                {
                    continue;
                }

                var sourceShade = replacementColorGroup < 0 ? sourcePixel : sourcePixel & 0x0f;
                var newShade = unchecked((byte)(sourceShade + shade));
                var crossedColorGroup = replacementColorGroup < 0
                    ? ((newShade ^ sourcePixel) & 0xf0) != 0
                    : (newShade & 0xf0) != 0;
                targetRow[targetX] = crossedColorGroup
                    ? (byte)0x0f
                    : replacementColorGroup < 0
                        ? newShade
                        : (byte)((replacementColorGroup << 4) | newShade);
            }
        }
    }

    public IndexedSurface Crop(int x, int y, int width, int height)
    {
        var result = new IndexedSurface(width, height);
        result.Blit(this, -x, -y, transparent: false);
        return result;
    }

    public void OffsetColors(int offset, int minimum = -1, int maximum = -1, int multiplier = 1)
    {
        ArgumentOutOfRangeException.ThrowIfZero(multiplier);

        for (var index = 0; index < _pixels.Length; index++)
        {
            var pixel = _pixels[index];
            if (pixel == 0)
            {
                continue;
            }

            var shifted = offset > 0 ? pixel * multiplier + offset : (pixel + offset) / multiplier;
            if (minimum != -1 && shifted < minimum)
            {
                shifted = minimum;
            }
            else if (maximum != -1 && shifted > maximum)
            {
                shifted = maximum;
            }

            _pixels[index] = unchecked((byte)shifted);
        }
    }

    public void OffsetColorBlocks(int offset, int blockSize, int multiplier = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        ArgumentOutOfRangeException.ThrowIfZero(multiplier);

        for (var index = 0; index < _pixels.Length; index++)
        {
            var pixel = _pixels[index];
            if (pixel == 0)
            {
                continue;
            }

            var minimum = pixel / blockSize * blockSize;
            var maximum = minimum + blockSize;
            var shifted = offset > 0 ? pixel * multiplier + offset : (pixel + offset) / multiplier;
            _pixels[index] = unchecked((byte)Math.Clamp(shifted, minimum, maximum));
        }
    }

    public void InvertColors(byte midpoint)
    {
        for (var index = 0; index < _pixels.Length; index++)
        {
            var pixel = _pixels[index];
            if (pixel != 0)
            {
                _pixels[index] = unchecked((byte)(pixel + (2 * (midpoint - pixel))));
            }
        }
    }

    private void SetPixelIfVisible(int x, int y, byte color)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height)
        {
            _pixels[checked((y * Width) + x)] = color;
        }
    }

    private void ValidateCoordinates(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
    }
}

public readonly record struct IndexedPoint(int X, int Y);
