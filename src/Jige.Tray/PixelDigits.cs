namespace Jige.Tray;

internal static class PixelDigits
{
    // 每行的低六位按从左到右存放，笔画对齐 16px 图标的整数像素。
    private static ReadOnlySpan<byte> Rows =>
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

    public static void Draw(Span<uint> pixels, IconAppearance icon)
    {
        uint ink = icon.Ink.Premultiplied;
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
            DrawDigit(pixels, value / 10, 1, top, ink);
        DrawDigit(pixels, value % 10, value >= 10 ? 9 : 5, top, ink);

        if (icon.Underline)
        {
            pixels.Slice(13 * 16 + 3, 10).Fill(ink);
            pixels.Slice(14 * 16 + 3, 10).Fill(ink);
        }
    }

    private static void DrawDigit(Span<uint> pixels, int digit, int left, int top, uint ink)
    {
        for (int y = 0; y < 10; y++)
        {
            int row = Rows[digit * 10 + y];
            for (int x = 0; x < 6; x++)
            {
                if ((row & (1 << x)) != 0)
                    pixels[(top + y) * 16 + left + x] = ink;
            }
        }
    }
}
