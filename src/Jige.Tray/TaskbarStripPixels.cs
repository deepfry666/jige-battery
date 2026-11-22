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

    public static uint[] Render(TaskbarStripLayout layout, bool dark, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        int width = layout.Width * scale;
        var pixels = new uint[width * TaskbarStripLayout.Height * scale];
        var palette = TaskbarStripPalette.Create(dark);
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
                Fill(separator.X, separator.Y, separator.Width, separator.Height, palette.Separator.Premultiplied);
        }
        return pixels;

        void Draw(string[] rows, int left, int top, Color ink)
        {
            uint argb = ink.Premultiplied;
            for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (rows[y][x] == 'X')
                    Fill(left + x, top + y, 1, 1, argb);
        }

        void Fill(int x, int y, int columns, int rows, uint argb)
        {
            for (int row = y * scale; row < (y + rows) * scale; row++)
                pixels.AsSpan(row * width + x * scale, columns * scale).Fill(argb);
        }
    }
}
