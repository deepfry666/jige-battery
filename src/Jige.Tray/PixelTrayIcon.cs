using Jige.Core;

namespace Jige.Tray;

internal static class PixelTrayIcon
{
    private const int Size = 16;

    private static readonly string[] Mouse =
    [
        "..####..",
        ".##..##.",
        "##.##.##",
        "#..##..#",
        "#..##..#",
        "########",
        "#......#",
        "#......#",
        "#......#",
        "##....##",
        ".##..##.",
        "..####.."
    ];

    private static readonly string[] Keyboard =
    [
        ".############.",
        "##..........##",
        "#............#",
        "#.#.#.#.#.#..#",
        "#............#",
        "#..#.#.#.#.#.#",
        "#............#",
        "#...######...#",
        "#............#",
        "##..........##",
        ".############."
    ];

    private static readonly string[] Headset =
    [
        "...######...",
        "..##....##..",
        ".##......##.",
        ".#........#.",
        ".#........#.",
        "##........##",
        "###......###",
        "###......###",
        "###......###",
        "###......###",
        ".##......##.",
        "..#......#.."
    ];

    private static readonly string[] Other =
    [
        "..########..",
        ".##......##.",
        "##........##",
        "#..##......#",
        "#..##......#",
        "#..........#",
        "#...####...#",
        "#..........#",
        "##........##",
        ".##......##.",
        "..########.."
    ];

    public static void Draw(Span<uint> pixels, IconAppearance icon)
    {
        if (icon.Kind is not { } kind)
        {
            Fill(pixels, new(5, 7, 6, 2), icon.FillInk);
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
        uint ink = icon.GlyphInk.Premultiplied;
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < rows[y].Length; x++)
            {
                if (rows[y][x] == '#')
                    pixels[(layout.Glyph.Y + y) * Size + layout.Glyph.X + x] = ink;
            }
        }

        Fill(pixels, layout.Track, icon.TrackInk);
        Fill(pixels, layout.Fill, icon.FillInk);
    }

    private static void Fill(Span<uint> pixels, IconRect rect, Color color)
    {
        uint ink = color.Premultiplied;
        for (int y = rect.Y; y < rect.Bottom; y++)
            pixels.Slice(y * Size + rect.X, rect.Width).Fill(ink);
    }
}
