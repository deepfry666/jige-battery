using Jige.Core;

namespace Jige.Tray;

internal static class PixelDigits
{
    // 每行的低六位按从左到右存放，笔画对齐 16px 图标的整数像素（6×10）。
    private static ReadOnlySpan<byte> Rows10 =>
    [
        30, 63, 51, 51, 51, 51, 51, 51, 63, 30,
        12, 14, 15, 12, 12, 12, 12, 12, 63, 63,
        30, 63, 51, 48, 48, 24, 12, 6, 63, 63,
        31, 63, 48, 48, 30, 30, 48, 48, 63, 31,
        51, 51, 51, 51, 63, 62, 48, 48, 48, 48,
        63, 63, 3, 3, 31, 63, 48, 48, 63, 31,
        30, 63, 3, 3, 31, 63, 51, 51, 63, 30,
        63, 63, 48, 48, 24, 24, 12, 12, 6, 6,
        30, 63, 51, 51, 30, 30, 51, 51, 63, 30,
        30, 63, 51, 51, 63, 62, 48, 48, 63, 30
    ];

    // 16px 充电带底线时使用的 6×9 数字，确保上方设备图形与底部充电线之间各留 1px 空隙。
    private static ReadOnlySpan<byte> Rows9 =>
    [
        30, 63, 51, 51, 51, 51, 51, 63, 30,
        12, 14, 15, 12, 12, 12, 12, 63, 63,
        30, 63, 51, 48, 24, 12, 6, 63, 63,
        31, 63, 48, 30, 30, 48, 48, 63, 31,
        51, 51, 51, 63, 62, 48, 48, 48, 48,
        63, 63, 3, 31, 63, 48, 48, 63, 31,
        30, 63, 3, 31, 63, 51, 51, 63, 30,
        63, 63, 48, 24, 24, 12, 12, 6, 6,
        30, 63, 51, 30, 30, 51, 51, 63, 30,
        30, 63, 51, 63, 62, 48, 48, 63, 30
    ];

    // 16px 满电 100：1(3px) + 空(1px) + 0(5px) + 空(1px) + 0(5px) = 15px 宽 × 10px 高，不截断。
    private static readonly string[] Condensed100 =
    [
        ".##..###...###.",
        "###.#####.#####",
        ".##.##.##.##.##",
        ".##.##.##.##.##",
        ".##.##.##.##.##",
        ".##.##.##.##.##",
        ".##.##.##.##.##",
        ".##.##.##.##.##",
        ".##.#####.#####",
        ".##..###...###."
    ];

    // 16px 常规 5px 高设备图形：深浅任务栏 100% 缩放下轮廓分明。
    private static readonly string[] Mouse5 =
    [
        ".#.#.",
        "##.##",
        "#####",
        "#####",
        ".###."
    ];

    private static readonly string[] Keyboard5 =
    [
        ".#########.",
        "#.#.#.#.#.#",
        "###########",
        "###.....###",
        ".#########."
    ];

    private static readonly string[] Headset5 =
    [
        "..######..",
        ".#......#.",
        "##......##",
        "##......##",
        ".#......#."
    ];

    private static readonly string[] Other5 =
    [
        "########.",
        "#......##",
        "#.####.##",
        "#......##",
        "########."
    ];

    // 16px 充电带底线时的紧凑 4px 高设备图形。
    private static readonly string[] Mouse4 =
    [
        ".#.#.",
        "##.##",
        "#####",
        ".###."
    ];

    private static readonly string[] Keyboard4 =
    [
        "#.#.#.#.#.#",
        "###########",
        "###.....###",
        ".#########."
    ];

    private static readonly string[] Headset4 =
    [
        "..######..",
        ".#......#.",
        "##......##",
        "##......##"
    ];

    private static readonly string[] Other4 =
    [
        "#######.",
        "#.....##",
        "#.....##",
        "#######."
    ];

    public static void Draw(Span<uint> pixels, IconAppearance icon)
    {
        var layout = TrayIconLayout.Compute(16, icon);
        uint ink = icon.Ink.Premultiplied;
        if (!layout.HasGlyph)
        {
            DrawFallback(pixels, icon, ink);
            return;
        }

        uint glyphInk = icon.EffectiveGlyphInk.Premultiplied;
        DrawMask(pixels, GlyphRows16(icon.Kind!.Value, compact: layout.HasUnderline), layout.Glyph.X, layout.Glyph.Y, glyphInk);

        if (icon.Full)
        {
            DrawMask(pixels, Condensed100, 0, layout.Digits.Y, ink);
            return;
        }

        if (icon.Percentage is not { } value)
        {
            int midY = layout.Digits.Y + (layout.Digits.Height - 2) / 2;
            pixels.Slice(midY * 16 + 5, 6).Fill(ink);
            pixels.Slice((midY + 1) * 16 + 5, 6).Fill(ink);
            return;
        }

        int digitHeight = layout.Digits.Height;
        int top = layout.Digits.Y;
        if (value >= 10)
            DrawDigit(pixels, value / 10, 1, top, digitHeight, ink);
        DrawDigit(pixels, value % 10, value >= 10 ? 9 : 5, top, digitHeight, ink);

        if (layout.HasUnderline)
        {
            for (int y = layout.Underline.Y; y < layout.Underline.Bottom; y++)
                pixels.Slice(y * 16 + layout.Underline.X, layout.Underline.Width).Fill(ink);
        }
    }

    internal static string[] GlyphRows16(DeviceKind kind, bool compact) => (kind, compact) switch
    {
        (DeviceKind.Mouse, false) => Mouse5,
        (DeviceKind.Keyboard, false) => Keyboard5,
        (DeviceKind.Headset, false) => Headset5,
        (_, false) => Other5,
        (DeviceKind.Mouse, true) => Mouse4,
        (DeviceKind.Keyboard, true) => Keyboard4,
        (DeviceKind.Headset, true) => Headset4,
        (_, true) => Other4
    };

    private static void DrawFallback(Span<uint> pixels, IconAppearance icon, uint ink)
    {
        if (icon.Full)
        {
            for (int y = 4; y <= 11; y++)
            {
                for (int x = 1; x <= 12; x++)
                {
                    bool outline = y is 4 or 11 ? x is > 1 and < 12 : x is 1 or 12;
                    if (outline || (y is >= 6 and <= 9 && x is >= 3 and <= 10))
                        pixels[y * 16 + x] = ink;
                }
            }
            pixels[6 * 16 + 14] = pixels[7 * 16 + 14] = pixels[8 * 16 + 14] = pixels[9 * 16 + 14] = ink;
            return;
        }

        if (icon.Percentage is not { } value)
        {
            pixels.Slice(7 * 16 + 5, 6).Fill(ink);
            pixels.Slice(8 * 16 + 5, 6).Fill(ink);
            return;
        }

        int top = icon.Underline ? 2 : 3;
        if (value >= 10)
            DrawDigit(pixels, value / 10, 1, top, 10, ink);
        DrawDigit(pixels, value % 10, value >= 10 ? 9 : 5, top, 10, ink);

        if (icon.Underline)
        {
            pixels.Slice(13 * 16 + 3, 10).Fill(ink);
            pixels.Slice(14 * 16 + 3, 10).Fill(ink);
        }
    }

    private static void DrawMask(Span<uint> pixels, string[] rows, int left, int top, uint ink)
    {
        for (int y = 0; y < rows.Length; y++)
        {
            string row = rows[y];
            for (int x = 0; x < row.Length; x++)
            {
                if (row[x] == '#')
                    pixels[(top + y) * 16 + left + x] = ink;
            }
        }
    }

    private static void DrawDigit(Span<uint> pixels, int digit, int left, int top, int height, uint ink)
    {
        var table = height == 9 ? Rows9 : Rows10;
        int stride = height == 9 ? 9 : 10;
        for (int y = 0; y < stride; y++)
        {
            int row = table[digit * stride + y];
            for (int x = 0; x < 6; x++)
            {
                if ((row & (1 << x)) != 0)
                    pixels[(top + y) * 16 + left + x] = ink;
            }
        }
    }
}
