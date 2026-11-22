using Jige.Core;

namespace Jige.Tray;

internal static class TaskbarStripPixels
{
    // UpdateLayeredWindow 对 alpha == 0 的像素默认点击穿透；用 alpha=1（视觉完全透明）保持整条区域的悬停与点击命中。
    public const uint LayeredHitPixel = 0x01000000u;

    public static void FillLayeredHitAlpha(Span<uint> pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] == 0)
                pixels[i] = LayeredHitPixel;
        }
    }

    // design/mockups/tray-v4-antigravity.html · 方案一的原始像素稿。
    private static readonly string[] Keyboard =
    [
        "...........",
        "...........",
        ".XXXXXXXXX.",
        "XXXXXXXXXXX",
        "X.X.X.X.X.X",
        "XXXXXXXXXXX",
        "XX.X.X.X.XX",
        "XXXXXXXXXXX",
        "XXX.....XXX",
        ".XXXXXXXXX.",
        "...........",
    ];

    private static readonly string[] Mouse =
    [
        "...XXXXX...",
        "..XXX.XXX..",
        ".XXXX.XXXX.",
        ".XXXX.XXXX.",
        ".XXXX.XXXX.",
        ".XXXXXXXXX.",
        ".XXXXXXXXX.",
        ".XXXXXXXXX.",
        ".XXXXXXXXX.",
        "..XXXXXXX..",
        "...XXXXX...",
    ];

    private static readonly string[] Headset =
    [
        "..XXXXXXX..",
        ".XX.....XX.",
        "XX.......XX",
        "XX.......XX",
        "XX.......XX",
        "XXX.....XXX",
        "XXX.....XXX",
        "XXX.....XXX",
        "XXX.....XXX",
        ".XX.....XX.",
        "...........",
    ];

    private static readonly string[] Other =
    [
        "...........",
        "..XXXXXXX..",
        ".XXXXXXXXX.",
        ".XX.....XX.",
        ".XX.XXX.XX.",
        ".XX.XXX.XX.",
        ".XX.....XX.",
        ".XXXXXXXXX.",
        ".XX.XXX.XX.",
        "..XXXXXXX..",
        "..........."
    ];

    private static readonly string[] Battery =
    [
        "...........",
        "...........",
        ".XXXXXXXX..",
        "X........X.",
        "X........XX",
        "X........XX",
        "X........XX",
        "X........X.",
        ".XXXXXXXX..",
        "...........",
        "..........."
    ];

    private static readonly string[] Bolt = ["...XX", "..XX.", ".XX..", "XXXXX", "..XX.", ".XX..", "XX..."];

    private static readonly string[][] Digits =
    [
        [".XXXX.", "XX..XX", "XX..XX", "XX..XX", "XX..XX", "XX..XX", "XX..XX", "XX..XX", ".XXXX."],
        ["..XX..", ".XXX..", "XX.X..", "..XX..", "..XX..", "..XX..", "..XX..", "..XX..", ".XXXX."],
        [".XXXX.", "XX..XX", "....XX", "...XX.", "..XX..", ".XX...", "XX....", "XX....", "XXXXXX"],
        [".XXXX.", "XX..XX", "....XX", "..XXX.", "....XX", "....XX", "....XX", "XX..XX", ".XXXX."],
        ["...XX.", "..XXX.", ".XXXX.", "XX.XX.", "XX.XX.", "XXXXXX", "...XX.", "...XX.", "...XX."],
        ["XXXXXX", "XX....", "XX....", "XXXXX.", "....XX", "....XX", "....XX", "XX..XX", ".XXXX."],
        [".XXXX.", "XX....", "XX....", "XXXXX.", "XX..XX", "XX..XX", "XX..XX", "XX..XX", ".XXXX."],
        ["XXXXXX", "....XX", "...XX.", "...XX.", "..XX..", "..XX..", ".XX...", ".XX...", ".XX..."],
        [".XXXX.", "XX..XX", "XX..XX", ".XXXX.", "XX..XX", "XX..XX", "XX..XX", "XX..XX", ".XXXX."],
        [".XXXX.", "XX..XX", "XX..XX", ".XXXXX", "....XX", "....XX", "....XX", "XX..XX", ".XXXX."],
        ["......", "XX...X", "XX..X.", "...X..", "..X...", ".X....", "X...XX", "....XX", "......"],
        ["......", "......", "......", "......", "XXXXXX", "XXXXXX", "......", "......", "......"]
    ];

    public static uint[] Render(TaskbarStripLayout layout, bool dark, int scale) =>
        Render(layout, TaskbarStripPalette.Create(dark), scale);

    public static uint[] Render(TaskbarStripLayout layout, TaskbarStripPalette palette, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        int width = layout.Width * scale;
        var pixels = new uint[width * TaskbarStripLayout.Height * scale];
        bool badges = palette.Style == TaskbarStripStyle.Badges;
        bool meter = palette.Style == TaskbarStripStyle.Meter;

        if (badges)
        {
            foreach (var segment in layout.Segments)
            {
                if (segment.Width < 6)
                    continue;
                var (fill, border) = palette.BadgeSurface(segment.Device.Tone, segment.Device.ShowBolt);
                DrawPlate(segment.Left, segment.Width, fill.Premultiplied, border.Premultiplied);
            }
        }
        else if (palette.Plate is { } plate && layout.Width >= 6)
        {
            DrawPlate(0, layout.Width, plate.Premultiplied, palette.PlateBorder?.Premultiplied ?? 0u);
        }

        int yOffset = meter ? -2 : 0;
        foreach (var segment in layout.Segments)
        {
            var (icon, text) = palette.Ink(segment.Device.Tone, segment.Device.ShowBolt);
            var shape = segment.Device.IsPlaceholder ? Battery : segment.Device.Kind switch
            {
                DeviceKind.Keyboard => Keyboard,
                DeviceKind.Mouse => Mouse,
                DeviceKind.Headset => Headset,
                _ => Other
            };
            Draw(shape, segment.Icon.X, segment.Icon.Y + yOffset, icon);
            if (segment.Bolt is { } bolt)
                Draw(Bolt, bolt.X, bolt.Y + yOffset, palette.Charging);
            int x = segment.Text.X;
            foreach (char glyph in segment.Device.Text)
            {
                int index = glyph switch { '%' => 10, '-' => 11, _ => glyph - '0' };
                Draw(Digits[index], x, segment.Text.Y + yOffset, text);
                x += TaskbarStripLayout.GlyphWidth + 1;
            }
            if (meter)
                DrawMeter(segment, text);
            if (!badges && segment.Separator is { } separator)
                Blend(separator.X, separator.Y, separator.Width, separator.Height, palette.Separator.Premultiplied);
        }
        return pixels;

        void DrawPlate(int leftX, int logicalWidth, uint plateArgb, uint borderArgb)
        {
            int logicalHeight = TaskbarStripLayout.Height;
            uint rimArgb = borderArgb != 0 ? CompositeOver(borderArgb, plateArgb) : plateArgb;
            for (int y = 0; y < logicalHeight; y++)
            {
                int inset = y == 0 || y == logicalHeight - 1 ? 2 : y == 1 || y == logicalHeight - 2 ? 1 : 0;
                if (y == 0 || y == logicalHeight - 1)
                {
                    Fill(leftX + inset, y, logicalWidth - inset * 2, 1, rimArgb);
                }
                else
                {
                    Fill(leftX + inset, y, 1, 1, rimArgb);
                    Fill(leftX + inset + 1, y, logicalWidth - (inset + 1) * 2, 1, plateArgb);
                    Fill(leftX + logicalWidth - 1 - inset, y, 1, 1, rimArgb);
                }
            }
        }

        void DrawMeter(TaskbarStripSegment segment, Color litColor)
        {
            const int cells = 4, gap = 1, barY = 17, barHeight = 2;
            int barLeft = segment.Icon.X;
            int barWidth = segment.Text.Right - barLeft;
            if (barWidth < cells * 2)
                return;

            int lit = segment.Device.Percentage is { } percent && percent > 0
                ? Math.Clamp((percent * cells + 50) / 100, 1, cells)
                : 0;
            uint litArgb = litColor.Premultiplied;
            uint dimArgb = palette.Foreground.WithAlpha(palette.UsesLightInk ? (byte)56 : (byte)48).Premultiplied;
            for (int i = 0; i < cells; i++)
            {
                int cellLeft = barLeft + i * (barWidth + gap) / cells;
                int cellRight = barLeft + (i + 1) * (barWidth + gap) / cells - gap;
                if (cellRight > cellLeft)
                    Blend(cellLeft, barY, cellRight - cellLeft, barHeight, i < lit ? litArgb : dimArgb);
            }
        }

        void Draw(string[] rows, int left, int top, Color ink)
        {
            uint argb = ink.Premultiplied;
            for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (rows[y][x] == 'X')
                    Blend(left + x, top + y, 1, 1, argb);
        }

        void Fill(int x, int y, int columns, int rows, uint argb)
        {
            for (int row = y * scale; row < (y + rows) * scale; row++)
                pixels.AsSpan(row * width + x * scale, columns * scale).Fill(argb);
        }

        void Blend(int x, int y, int columns, int rows, uint argb)
        {
            if ((argb >> 24) == 255)
            {
                Fill(x, y, columns, rows, argb);
                return;
            }
            if ((argb >> 24) == 0)
                return;
            for (int row = y * scale; row < (y + rows) * scale; row++)
            {
                var span = pixels.AsSpan(row * width + x * scale, columns * scale);
                for (int i = 0; i < span.Length; i++)
                    span[i] = CompositeOver(argb, span[i]);
            }
        }
    }

    internal static uint CompositeOver(uint src, uint dst)
    {
        uint srcA = src >> 24;
        if (srcA == 255 || dst == 0)
            return src;
        if (srcA == 0)
            return dst;
        uint invA = 255u - srcA;
        uint outA = srcA + (((dst >> 24) & 255) * invA + 127) / 255;
        uint outR = ((src >> 16) & 255) + (((dst >> 16) & 255) * invA + 127) / 255;
        uint outG = ((src >> 8) & 255) + (((dst >> 8) & 255) * invA + 127) / 255;
        uint outB = (src & 255) + ((dst & 255) * invA + 127) / 255;
        return (Math.Min(255u, outA) << 24) |
               (Math.Min(255u, outR) << 16) |
               (Math.Min(255u, outG) << 8) |
               Math.Min(255u, outB);
    }
}
