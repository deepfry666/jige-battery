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
