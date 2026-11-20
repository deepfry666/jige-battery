using Jige.Core;

namespace Jige.Tray;

internal readonly record struct IconRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

internal readonly record struct TrayIconLayout(IconRect Glyph, int Waterline)
{
    public static TrayIconLayout Compute(int size, DeviceKind kind, int? percentage)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);
        var (width, height) = TrayDeviceShape.Dimensions(kind);
        int glyphWidth = Scale(width, size);
        int glyphHeight = Scale(height, size);
        var glyph = new IconRect((size - glyphWidth) / 2, (size - glyphHeight) / 2, glyphWidth, glyphHeight);

        // 按剪影高度取最近的物理像素行，恰好半行时向上取整。有读数且非零时至少一行。
        int percent = Math.Clamp(percentage ?? 0, 0, 100);
        int filledRows = percent == 0 ? 0 : Math.Max(1, Presentation.FilledCells(percent, glyphHeight));
        return new(glyph, glyph.Bottom - filledRows);
    }

    private static int Scale(int pixelsAt16, int size) => (pixelsAt16 * size + 8) / 16;
}
