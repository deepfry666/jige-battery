using Jige.Core;

namespace Jige.Tray;

internal static class PixelTrayIcon
{
    private const int Size = 16;

    // BIG 位图裁到实际外接矩形，TrayIconLayout 负责与 16px 图标对齐。
    private static readonly string[] Mouse =
    [
        "..XXXXXX..",
        ".XXXXXXXX.",
        "XXXX..XXXX",
        "XXXX..XXXX",
        "XXXX..XXXX",
        "XXXXXXXXXX",
        "..........",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        "XXXXXXXXXX",
        ".XXXXXXXX.",
        "..XXXXXX.."
    ];

    private static readonly string[] Keyboard =
    [
        ".XXXXXXXXXXXXX.",
        "XXXXXXXXXXXXXXX",
        "XX.X.X.X.X.X.XX",
        "XXXXXXXXXXXXXXX",
        "XXX.X.X.X.X.XXX",
        "XXXXXXXXXXXXXXX",
        "XXXX.......XXXX",
        "XXXXXXXXXXXXXXX",
        ".XXXXXXXXXXXXX."
    ];

    private static readonly string[] Headset =
    [
        ".....XXXXXX.....",
        "...XXXXXXXXXX...",
        "..XXX......XXX..",
        ".XXX........XXX.",
        ".XX..........XX.",
        ".XX..........XX.",
        "XXXXX......XXXXX",
        "XXXXX......XXXXX",
        "XXXXX......XXXXX",
        "XXXXX......XXXXX",
        "XXXXX......XXXXX",
        "XXXXX......XXXXX",
        ".XXX........XXX."
    ];

    private static readonly string[] Other =
    [
        "..XXXXXXXXXX..",
        ".XXXXXXXXXXXX.",
        "XXXXXXXXXXXXXX",
        "XXX..XXXXXXXXX",
        "XXX..XXXXXXXXX",
        "XXXXXXXXXXXXXX",
        "XXXXXXXXXXXXXX",
        "XXXXX......XXX",
        "XXXXX......XXX",
        "XXXXXXXXXXXXXX",
        ".XXXXXXXXXXXX.",
        "..XXXXXXXXXX.."
    ];

    // 绘制阶段（像素版和 Direct2D）都产出预乘 alpha，而 CreateIconIndirect 要的是未预乘的 alpha。
    // 不转换的话，半透明像素会被 Windows 再乘一次 alpha：28% 的暗色实际只剩约 8%，在任务栏上几乎看不见。
    public static void ToStraightAlpha(Span<uint> pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            uint pixel = pixels[i];
            uint alpha = pixel >> 24;
            if (alpha is 0 or 255)
                continue;
            uint red = Math.Min(255u, (((pixel >> 16) & 255) * 255 + alpha / 2) / alpha);
            uint green = Math.Min(255u, (((pixel >> 8) & 255) * 255 + alpha / 2) / alpha);
            uint blue = Math.Min(255u, ((pixel & 255) * 255 + alpha / 2) / alpha);
            pixels[i] = (alpha << 24) | (red << 16) | (green << 8) | blue;
        }
    }

    public static void Draw(Span<uint> pixels, IconAppearance icon)
    {
        if (icon.Kind is not { } kind)
        {
            pixels.Slice(7 * Size + 5, 6).Fill(icon.FillInk.Premultiplied);
            pixels.Slice(8 * Size + 5, 6).Fill(icon.FillInk.Premultiplied);
            return;
        }

        var layout = TrayIconLayout.Compute(Size, kind, icon.Percentage);
        var rows = kind switch
        {
            DeviceKind.Mouse => Mouse,
            DeviceKind.Keyboard => Keyboard,
            DeviceKind.Headset => Headset,
            _ => Other
        };

        uint filled = icon.FillInk.Premultiplied;
        uint dim = icon.DimInk.Premultiplied;
        for (int row = 0; row < rows.Length; row++)
        {
            int y = layout.Glyph.Y + row;
            uint ink = y >= layout.Waterline ? filled : dim;
            for (int x = 0; x < rows[row].Length; x++)
            {
                if (rows[row][x] == 'X')
                    pixels[y * Size + layout.Glyph.X + x] = ink;
            }
        }
    }
}
