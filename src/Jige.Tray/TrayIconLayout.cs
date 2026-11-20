using Jige.Core;

namespace Jige.Tray;

internal readonly record struct IconRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool IsWithin(int size) =>
        !IsEmpty && X >= 0 && Y >= 0 && Right <= size && Bottom <= size;

    public bool Overlaps(IconRect other) =>
        !IsEmpty && !other.IsEmpty &&
        X < other.Right && Right > other.X &&
        Y < other.Bottom && Bottom > other.Y;
}

internal readonly record struct TrayIconLayout(IconRect Glyph, IconRect Track, IconRect Fill)
{
    public static TrayIconLayout Compute(int size, DeviceKind kind, int? percentage)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);
        var (width, height) = kind switch
        {
            DeviceKind.Mouse => (8, 12),
            DeviceKind.Keyboard => (14, 11),
            DeviceKind.Headset => (12, 12),
            _ => (12, 11)
        };
        int glyphWidth = Scale(width, size);
        int glyphHeight = Scale(height, size);
        int margin = Scale(1, size);
        int trackHeight = Scale(2, size);
        var glyph = new IconRect((size - glyphWidth) / 2, (Scale(12, size) - glyphHeight) / 2,
            glyphWidth, glyphHeight);
        var track = new IconRect(margin, size - trackHeight, size - 2 * margin, trackHeight);

        // 取最接近的整像素，恰好半个像素时向上取整。无读数的填充宽度为零。
        int fillWidth = Presentation.FilledCells(Math.Clamp(percentage ?? 0, 0, 100), track.Width);
        return new(glyph, track, track with { Width = fillWidth });
    }

    private static int Scale(int pixelsAt16, int size) => (pixelsAt16 * size + 8) / 16;
}
