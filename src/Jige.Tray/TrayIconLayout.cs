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

/// <summary>
/// 托盘图标方案 A 布局计算：上方为设备类型小图形，下方为电量数字（及充电底线）。
/// 保证在所有设备类型与 DPI 尺寸下图形、数字与底线互不重叠且不越界。
/// </summary>
internal readonly record struct TrayIconLayout(
    int Size,
    IconRect Glyph,
    IconRect Digits,
    IconRect Underline,
    bool HasGlyph,
    bool HasUnderline)
{
    public static TrayIconLayout Compute(int size, IconAppearance icon)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);
        if (icon.Kind is not { } kind)
            return ComputeFallback(size, icon);

        bool underline = icon.Underline;
        if (size == 16)
        {
            if (!underline)
            {
                int gw = GlyphWidth16(kind, compact: false);
                return new(16,
                    Glyph: new((16 - gw) / 2, 0, gw, 5),
                    Digits: new(0, 6, 16, 10),
                    Underline: default,
                    HasGlyph: true,
                    HasUnderline: false);
            }

            int compactW = GlyphWidth16(kind, compact: true);
            return new(16,
                Glyph: new((16 - compactW) / 2, 0, compactW, 4),
                Digits: new(0, 5, 16, 9),
                Underline: new(3, 15, 10, 1),
                HasGlyph: true,
                HasUnderline: true);
        }

        if (size < 16)
        {
            int smallDigitH = Math.Max(4, (size * 10) / 16);
            int smallUnderlineH = underline ? 1 : 0;
            int smallUnderlineGap = underline && size >= 12 ? 1 : 0;
            int smallGap = 1;
            int smallGlyphH = Math.Max(2, size - smallDigitH - smallGap - smallUnderlineGap - smallUnderlineH);
            smallDigitH = Math.Max(2, size - smallGlyphH - smallGap - smallUnderlineGap - smallUnderlineH);
            int smallGlyphW = ScalableGlyphWidth(kind, smallGlyphH, size);
            var smallGlyph = new IconRect((size - smallGlyphW) / 2, 0, smallGlyphW, smallGlyphH);
            var smallDigits = new IconRect(0, smallGlyph.Bottom + smallGap, size, smallDigitH);
            var smallUnderline = underline
                ? new IconRect(1, size - smallUnderlineH, Math.Max(2, size - 2), smallUnderlineH)
                : default;
            return new(size, smallGlyph, smallDigits, smallUnderline, HasGlyph: true, HasUnderline: underline);
        }

        // 24px -> 15px, 32px -> 20px, 48px -> 30px，保持与 16px 下 10px 数字等比例（5/8）。
        int digitH = (size * 10) / 16;
        if (!underline)
        {
            int gap = size >= 24 ? Math.Max(2, (int)MathF.Round(size / 16f, MidpointRounding.AwayFromZero)) : 1;
            int glyphH = size - digitH - gap;
            int glyphW = ScalableGlyphWidth(kind, glyphH, size);
            return new(size,
                Glyph: new((size - glyphW) / 2, 0, glyphW, glyphH),
                Digits: new(0, glyphH + gap, size, digitH),
                Underline: default,
                HasGlyph: true,
                HasUnderline: false);
        }

        int underlineH = Math.Max(1, size / 16);
        int underlineGap = Math.Max(1, size / 24);
        int glyphGap = Math.Max(1, size / 24);
        int chargingGlyphH = size - digitH - glyphGap - underlineGap - underlineH;
        int chargingGlyphW = ScalableGlyphWidth(kind, chargingGlyphH, size);
        int underlineW = Math.Clamp((int)MathF.Round(size * 0.68f, MidpointRounding.AwayFromZero), 6, size - 2);
        return new(size,
            Glyph: new((size - chargingGlyphW) / 2, 0, chargingGlyphW, chargingGlyphH),
            Digits: new(0, chargingGlyphH + glyphGap, size, digitH),
            Underline: new((size - underlineW) / 2, size - underlineH, underlineW, underlineH),
            HasGlyph: true,
            HasUnderline: true);
    }

    private static TrayIconLayout ComputeFallback(int size, IconAppearance icon)
    {
        if (size == 16)
        {
            if (icon.Full)
                return new(16, default, new(1, 4, 14, 8), default, HasGlyph: false, HasUnderline: false);
            if (icon.Percentage is not { } value)
                return new(16, default, new(5, 7, 6, 2), default, HasGlyph: false, HasUnderline: false);
            int top = icon.Underline ? 2 : 3;
            var digits = value >= 10 ? new IconRect(1, top, 14, 10) : new IconRect(5, top, 6, 10);
            var line = icon.Underline ? new IconRect(3, 13, 10, 2) : default;
            return new(16, default, digits, line, HasGlyph: false, HasUnderline: icon.Underline);
        }

        if (!icon.Underline)
            return new(size, default, new(0, 0, size, size), default, HasGlyph: false, HasUnderline: false);

        int underlineH = Math.Max(1, (int)MathF.Round(size * 0.1f, MidpointRounding.AwayFromZero));
        int underlineY = Math.Min(size - underlineH, (int)MathF.Round(size * 0.86f, MidpointRounding.AwayFromZero));
        int underlineW = Math.Clamp((int)MathF.Round(size * 0.68f, MidpointRounding.AwayFromZero), 4, size - 2);
        var underlineRect = new IconRect((size - underlineW) / 2, underlineY, underlineW, underlineH);
        return new(size, default, new(0, 0, size, Math.Max(1, underlineY - 1)), underlineRect,
            HasGlyph: false, HasUnderline: true);
    }

    internal static int GlyphWidth16(DeviceKind kind, bool compact) => kind switch
    {
        DeviceKind.Mouse => 5,
        DeviceKind.Keyboard => 11,
        DeviceKind.Headset => 10,
        _ => compact ? 8 : 9
    };

    private static int ScalableGlyphWidth(DeviceKind kind, int glyphHeight, int size)
    {
        float ratio = kind switch
        {
            DeviceKind.Mouse => 0.75f,
            DeviceKind.Keyboard => 2.00f,
            DeviceKind.Headset => 1.75f,
            _ => 1.65f
        };
        int maxWidth = kind == DeviceKind.Mouse
            ? Math.Max(4, Math.Min(size - 2, glyphHeight - 1))
            : Math.Max(4, size - 2);
        int width = Math.Clamp((int)MathF.Round(glyphHeight * ratio, MidpointRounding.AwayFromZero), 4, maxWidth);
        // 优先匹配奇偶性，使图形在水平方向严格居中；鼠标保持窄于高度。
        if ((size - width) % 2 != 0)
        {
            if (width + 1 <= maxWidth)
                width++;
            else if (width - 1 >= 4)
                width--;
        }
        return width;
    }
}
