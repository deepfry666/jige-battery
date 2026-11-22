using Jige.Core;

namespace Jige.Tray;

internal static class TaskbarStripPixels
{
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
        if (palette.Plate is { } plate && layout.Width >= 6)
            DrawPlate(plate.Premultiplied, palette.PlateBorder?.Premultiplied ?? 0u);

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
            Draw(shape, segment.Icon.X, segment.Icon.Y, icon);
            if (segment.Bolt is { } bolt)
                Draw(Bolt, bolt.X, bolt.Y, palette.Charging);
            int x = segment.Text.X;
            foreach (char glyph in segment.Device.Text)
            {
                int index = glyph switch { '%' => 10, '-' => 11, _ => glyph - '0' };
                Draw(Digits[index], x, segment.Text.Y, text);
                x += TaskbarStripLayout.GlyphWidth + 1;
            }
            if (segment.Separator is { } separator)
                Blend(separator.X, separator.Y, separator.Width, separator.Height, palette.Separator.Premultiplied);
        }
        return pixels;

        void DrawPlate(uint plateArgb, uint borderArgb)
        {
            int logicalWidth = layout.Width;
            int logicalHeight = TaskbarStripLayout.Height;
            uint rimArgb = borderArgb != 0 ? CompositeOver(borderArgb, plateArgb) : plateArgb;
            for (int y = 0; y < logicalHeight; y++)
            {
                int inset = y == 0 || y == logicalHeight - 1 ? 2 : y == 1 || y == logicalHeight - 2 ? 1 : 0;
                if (y == 0 || y == logicalHeight - 1)
                {
                    Fill(inset, y, logicalWidth - inset * 2, 1, rimArgb);
                }
                else
                {
                    Fill(inset, y, 1, 1, rimArgb);
                    Fill(inset + 1, y, logicalWidth - (inset + 1) * 2, 1, plateArgb);
                    Fill(logicalWidth - 1 - inset, y, 1, 1, rimArgb);
                }
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
