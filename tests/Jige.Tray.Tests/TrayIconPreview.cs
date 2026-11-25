using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using Jige.Tray;

namespace Jige.Tray.Tests;

internal static class TrayIconPreview
{
    // The 16px path is the production rasterizer. On macOS, sample the same vector
    // contours used by Direct2D, with optional supersampling for the PNG previews.
    public static uint[] Render(int size, IconAppearance icon, int samplesPerAxis = 1)
    {
        var pixels = new uint[size * size];
        if (size == 16)
        {
            PixelTrayIcon.Draw(pixels, icon);
            return pixels;
        }

        var kind = icon.Kind ?? throw new ArgumentException("预览需要设备类型", nameof(icon));
        var layout = TrayIconLayout.Compute(size, kind, icon.EffectiveFillPercentage);
        var transform = TrayDeviceShape.Transform(kind, layout.Glyph);
        var contours = TrayDeviceShape.Contours(kind).ToArray()
            .Select(contour => contour.Select(point => Vector2.Transform(point, transform)).ToArray())
            .ToArray();

        int total = samplesPerAxis * samplesPerAxis;
        for (int y = layout.Glyph.Y; y < layout.Glyph.Bottom; y++)
        {
            Color ink = y < layout.Waterline ? icon.DimInk : icon.FillInk;
            for (int x = layout.Glyph.X; x < layout.Glyph.Right; x++)
            {
                int covered = 0;
                for (int row = 0; row < samplesPerAxis; row++)
                for (int col = 0; col < samplesPerAxis; col++)
                {
                    float sampleX = x + (col + .5f) / samplesPerAxis;
                    float sampleY = y + (row + .5f) / samplesPerAxis;
                    if (Contains(contours, sampleX, sampleY))
                        covered++;
                }
                pixels[y * size + x] = ink.WithAlpha((byte)((ink.Alpha * covered + total / 2) / total)).Premultiplied;
            }
        }
        return pixels;
    }

    private static bool Contains(Vector2[][] contours, float x, float y)
    {
        bool inside = false;
        foreach (var contour in contours)
        {
            var previous = contour[^1];
            foreach (var next in contour)
            {
                if ((previous.Y > y) != (next.Y > y) &&
                    x < previous.X + (next.X - previous.X) * (y - previous.Y) / (next.Y - previous.Y))
                    inside = !inside;
                previous = next;
            }
        }
        return inside;
    }

    public static void SaveSheet(string path, int size, uint background, IconAppearance[][] icons)
    {
        const int padding = 12;
        int step = size + padding;
        int width = icons[0].Length * step + padding, height = icons.Length * step + padding;
        var sheet = new uint[width * height];
        for (int row = 0; row < icons.Length; row++)
        for (int col = 0; col < icons[row].Length; col++)
        {
            var pixels = Render(size, icons[row][col], samplesPerAxis: 8);
            for (int y = 0; y < size; y++)
                pixels.AsSpan(y * size, size).CopyTo(sheet.AsSpan(
                    (padding + row * step + y) * width + padding + col * step, size));
        }

        SaveRgb(path, width, height, background, sheet);
    }

    public static void SaveTaskbarStrip(string path, TaskbarStripDevice[] devices, bool dark, uint dpi) =>
        SaveTaskbarStrip(path, devices, dark ? TaskbarBackdrop.DefaultDarkRgb : TaskbarBackdrop.DefaultLightRgb, dpi);

    public static void SaveTaskbarStrip(string path, TaskbarStripDevice[] devices, uint backgroundRgb, uint dpi)
    {
        int scale = TaskbarStripLayout.PixelScale(dpi);
        var layout = TaskbarStripLayout.Build(devices);
        var backdrop = TaskbarBackdrop.Uniform(backgroundRgb, sampled: true);
        var palette = TaskbarStripPalette.Create(backdrop);
        bool dark = palette.UsesLightInk;
        int stripWidth = layout.Width * scale, stripHeight = TaskbarStripLayout.Height * scale;
        int width = (12 + layout.Width + 32) * scale, height = 48 * scale;
        var taskbar = new uint[width * height];
        var strip = TaskbarStripPixels.Render(layout, palette, scale);
        for (int y = 0; y < stripHeight; y++)
            strip.AsSpan(y * stripWidth, stripWidth).CopyTo(taskbar.AsSpan(
                ((height - stripHeight) / 2 + y) * width + 12 * scale, stripWidth));

        // Simulated notification chevron for size and placement reference.
        string[] arrow = ["...XX...", "..X..X..", ".X....X.", "X......X"];
        uint ink = dark ? 0xfff3f3f3 : 0xff1b1b1b;
        int x0 = width - 22 * scale, y0 = (height - arrow.Length * scale) / 2;
        for (int y = 0; y < arrow.Length; y++)
        for (int x = 0; x < arrow[y].Length; x++)
        {
            if (arrow[y][x] != 'X')
                continue;
            for (int row = 0; row < scale; row++)
                taskbar.AsSpan((y0 + y * scale + row) * width + x0 + x * scale, scale).Fill(ink);
        }
        SaveRgb(path, width, height, backgroundRgb, taskbar);
    }

    private static void SaveRgb(string path, int width, int height, uint background, uint[] pixels)
    {
        byte[] scanlines = new byte[height * (width * 3 + 1)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            uint pixel = pixels[y * width + x];
            int offset = y * (width * 3 + 1) + 1 + x * 3;
            uint transparency = 255 - (pixel >> 24);
            for (int channel = 0; channel < 3; channel++)
            {
                int shift = 16 - channel * 8;
                uint foreground = (pixel >> shift) & 255;
                uint backdrop = ((background >> shift) & 255) * transparency;
                scanlines[offset + channel] = (byte)(foreground + (backdrop + 127) / 255);
            }
        }

        using var png = File.Create(path);
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2; // RGB
        WriteChunk(png, "IHDR"u8, header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(scanlines);
        WriteChunk(png, "IDAT"u8, compressed.ToArray());
        WriteChunk(png, "IEND"u8, []);
    }

    private static void WriteChunk(Stream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        png.Write(word);
        png.Write(type);
        png.Write(data);
        uint crc = UpdateCrc(UpdateCrc(uint.MaxValue, type), data);
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc);
        png.Write(word);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        return crc;
    }
}
