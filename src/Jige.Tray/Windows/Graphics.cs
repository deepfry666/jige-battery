using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Jige.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.System.Com;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal static unsafe class Graphics
{
    public static void Release(void* value)
    {
        if (value != null)
            ((IUnknown*)value)->Release();
    }

    public static void Check(HRESULT result, string operation)
    {
        if (result.Failed)
            throw new COMException($"{operation}失败（{result}）", result);
    }

    public static D2D_RECT_F Rect(float x, float y, float width, float height) =>
        new() { left = x, top = y, right = x + width, bottom = y + height };

    public static D2D1_COLOR_F Ink(Color color) => new()
    {
        r = ((color.Rgb >> 16) & 255) / 255f,
        g = ((color.Rgb >> 8) & 255) / 255f,
        b = (color.Rgb & 255) / 255f,
        a = color.Alpha / 255f
    };

    public static D2D_POINT_2F Point(float x, float y) => new() { x = x, y = y };
}

[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class TextFactory : IDisposable
{
    private readonly Dictionary<(float Size, int Weight, bool Number, bool Wrap), nint> formats = [];
    private IDWriteFactory* factory;
    private IDWriteTypography* tabular;

    public TextFactory()
    {
        void* value = null;
        var iid = IDWriteFactory.IID_Guid;
        Graphics.Check(PInvoke.DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED, &iid, &value),
            "创建 DirectWrite 工厂");
        factory = (IDWriteFactory*)value;
        try
        {
            IDWriteTypography* typography = null;
            factory->CreateTypography(&typography);
            tabular = typography;
            tabular->AddFontFeature(new()
            {
                nameTag = DWRITE_FONT_FEATURE_TAG.DWRITE_FONT_FEATURE_TAG_TABULAR_FIGURES,
                parameter = 1
            });
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private IDWriteTextFormat* Format(float size, int weight, bool number, bool wrap = false)
    {
        var key = (size, weight, number, wrap);
        if (formats.TryGetValue(key, out nint existing))
            return (IDWriteTextFormat*)existing;

        IDWriteTextFormat* format = null;
        string family = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            ? (number ? "Segoe UI Variable Display" : "Segoe UI Variable Text")
            : "Segoe UI";
        factory->CreateTextFormat(family, null,
            (DWRITE_FONT_WEIGHT)weight, DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
            DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL, size, "zh-CN", &format);
        try
        {
            format->SetWordWrapping(wrap
                ? DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_EMERGENCY_BREAK
                : DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
            format->SetParagraphAlignment(wrap
                ? DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_NEAR
                : DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
            formats.Add(key, (nint)format);
            return format;
        }
        catch
        {
            Graphics.Release(format);
            throw;
        }
    }

    public IDWriteTextLayout* Layout(string text, float size, int weight, bool number, float width, float height,
        DWRITE_TEXT_ALIGNMENT alignment, bool ellipsis, bool wrap = false)
    {
        var format = Format(size, weight, number, wrap);
        IDWriteTextLayout* layout = null;
        factory->CreateTextLayout(text, (uint)text.Length, format, width, height, &layout);
        try
        {
            layout->SetTextAlignment(alignment);
            if (number)
                layout->SetTypography(tabular, new() { length = (uint)text.Length });
            if (ellipsis)
            {
                IDWriteInlineObject* sign = null;
                factory->CreateEllipsisTrimmingSign(format, &sign);
                try
                {
                    layout->SetTrimming(new DWRITE_TRIMMING
                    {
                        granularity = DWRITE_TRIMMING_GRANULARITY.DWRITE_TRIMMING_GRANULARITY_CHARACTER
                    }, sign);
                }
                finally
                {
                    Graphics.Release(sign);
                }
            }
            return layout;
        }
        catch
        {
            Graphics.Release(layout);
            throw;
        }
    }

    public void Dispose()
    {
        foreach (nint format in formats.Values)
            Graphics.Release((void*)format);
        formats.Clear();
        Graphics.Release(tabular);
        tabular = null;
        Graphics.Release(factory);
        factory = null;
    }
}

[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class Canvas : IDisposable
{
    private readonly ID2D1RenderTarget* target;
    private readonly TextFactory fonts;
    private ID2D1SolidColorBrush* brush;

    private static class Brand
    {
        internal const uint Size = 64;
        internal static readonly byte[] Pixels = Load();

        private static byte[] Load()
        {
            using var stream = typeof(Canvas).Assembly.GetManifestResourceStream("Jige.PanelIcon")!;
            var pixels = new byte[Size * Size * 4];
            stream.ReadExactly(pixels);
            return pixels;
        }
    }

    public Canvas(ID2D1RenderTarget* target, TextFactory fonts)
    {
        this.target = target;
        this.fonts = fonts;
        ID2D1SolidColorBrush* value = null;
        target->CreateSolidColorBrush(Graphics.Ink(new(0)), null, &value);
        brush = value;
        target->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE.D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
    }

    private ID2D1Brush* Brush(Color color)
    {
        brush->SetColor(Graphics.Ink(color));
        return (ID2D1Brush*)brush;
    }

    public void Fill(float x, float y, float w, float h, float radius, Color color)
    {
        var rect = new D2D1_ROUNDED_RECT { rect = Graphics.Rect(x, y, w, h), radiusX = radius, radiusY = radius };
        target->FillRoundedRectangle(in rect, Brush(color));
    }

    public void Stroke(float x, float y, float w, float h, float radius, Color color, float thickness = 1)
    {
        var rect = new D2D1_ROUNDED_RECT { rect = Graphics.Rect(x, y, w, h), radiusX = radius, radiusY = radius };
        target->DrawRoundedRectangle(in rect, Brush(color), thickness, null);
    }

    public void PushClip(PanelRect rect) =>
        target->PushAxisAlignedClip(Graphics.Rect(rect.X, rect.Y, rect.Width, rect.Height),
            D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);

    public void PopClip() => target->PopAxisAlignedClip();

    public void Line(float x1, float y1, float x2, float y2, Color color, float thickness = 1) =>
        target->DrawLine(Graphics.Point(x1, y1), Graphics.Point(x2, y2), Brush(color), thickness, null);

    public void Cubic(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, Color color)
    {
        float x = x0, y = y0;
        for (int i = 1; i <= 12; i++)
        {
            float t = i / 12f, u = 1 - t;
            float nextX = u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3;
            float nextY = u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3;
            Line(x, y, nextX, nextY, color, 1.3f);
            x = nextX;
            y = nextY;
        }
    }

    public void Dot(float x, float y, float radius, Color color)
    {
        var circle = new D2D1_ELLIPSE { point = Graphics.Point(x, y), radiusX = radius, radiusY = radius };
        target->FillEllipse(in circle, Brush(color));
    }

    public void Ring(float x, float y, float radius, Color color, float thickness)
    {
        var circle = new D2D1_ELLIPSE { point = Graphics.Point(x, y), radiusX = radius, radiusY = radius };
        target->DrawEllipse(in circle, Brush(color), thickness, null);
    }

    public void Arc(float x, float y, float radius, float from, float to, Color color, float thickness)
    {
        const int segments = 18;
        float ax = x + radius * MathF.Cos(from), ay = y + radius * MathF.Sin(from);
        for (int i = 1; i <= segments; i++)
        {
            float angle = from + (to - from) * i / segments;
            float bx = x + radius * MathF.Cos(angle), by = y + radius * MathF.Sin(angle);
            Line(ax, ay, bx, by, color, thickness);
            ax = bx;
            ay = by;
        }
    }

    public void BrandIcon(float x, float y, float size)
    {
        ID2D1Bitmap* bitmap = null;
        var format = new D2D1_BITMAP_PROPERTIES
        {
            pixelFormat = new()
            {
                format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED
            },
            dpiX = 96,
            dpiY = 96
        };
        fixed (byte* pixels = Brand.Pixels)
            target->CreateBitmap(new() { width = Brand.Size, height = Brand.Size },
                pixels, Brand.Size * 4, in format, &bitmap);
        try
        {
            var bounds = Graphics.Rect(x, y, size, size);
            target->DrawBitmap(bitmap, &bounds, 1,
                D2D1_BITMAP_INTERPOLATION_MODE.D2D1_BITMAP_INTERPOLATION_MODE_LINEAR, null);
        }
        finally
        {
            Graphics.Release(bitmap);
        }
    }

    public void Battery(float x, float y, float scale, Color ink)
    {
        Stroke(x + scale, y + 4 * scale, 12.5f * scale, 8 * scale, 1.8f * scale, ink, 1.3f * scale);
        Fill(x + 14.2f * scale, y + 6.4f * scale, 1.3f * scale, 3.2f * scale, .6f * scale, ink);
        Fill(x + 2.6f * scale, y + 5.6f * scale, 9.3f * scale, 4.8f * scale, .8f * scale, ink);
    }

    public void DeviceTile(DeviceKind kind, float x, float y, float size, Color fill, Color ink)
    {
        float radius = size >= 36 ? 8 : size >= 30 ? 7 : 6;
        Fill(x, y, size, size, radius, fill);
        float cx = x + size / 2, cy = y + size / 2;
        float k = size <= 26 ? 0.78f : 1f;
        float stroke = size <= 26 ? 1.15f : 1.2f;
        switch (kind)
        {
            case DeviceKind.Mouse:
                Stroke(cx - 4.5f * k, cy - 7.2f * k, 9 * k, 14.3f * k, 4.5f * k, ink, stroke);
                Line(cx, cy - 7.2f * k, cx, cy - 2.3f * k, ink, stroke);
                break;
            case DeviceKind.Headset:
                Arc(cx, cy, 6 * k, MathF.PI, 2 * MathF.PI, ink, stroke);
                Stroke(cx - 7 * k, cy + 1.5f * k, 3.4f * k, 5.2f * k, 1.2f * k, ink, stroke);
                Stroke(cx + 3.6f * k, cy + 1.5f * k, 3.4f * k, 5.2f * k, 1.2f * k, ink, stroke);
                break;
            case DeviceKind.Keyboard:
                Stroke(cx - 7 * k, cy - 4.5f * k, 14 * k, 9 * k, 2 * k, ink, stroke);
                for (int i = -2; i <= 2; i++)
                    Dot(cx + i * 2.3f * k, cy - 1 * k, .5f * k, ink);
                Line(cx - 3 * k, cy + 2 * k, cx + 3 * k, cy + 2 * k, ink, stroke);
                break;
            default:
                Battery(cx - 8 * k, cy - 8 * k, k, ink);
                break;
        }
    }

    public void TrayDeviceGlyph(DeviceKind kind, IconRect rect, Color ink)
    {
        float x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;
        float stroke = MathF.Max(0.8f, h * 0.12f);
        float half = stroke / 2f;
        PushClip(new PanelRect(x, y, w, h));
        try
        {
            switch (kind)
            {
                case DeviceKind.Mouse:
                    Stroke(x + half, y + half, w - stroke, h - stroke, w * 0.42f, ink, stroke);
                    Line(x + w * 0.50f, y + half, x + w * 0.50f, y + h * 0.48f, ink, stroke);
                    Line(x + half, y + h * 0.48f, x + w - half, y + h * 0.48f, ink, stroke * 0.8f);
                    break;
                case DeviceKind.Keyboard:
                    Stroke(x + half, y + half, w - stroke, h - stroke, h * 0.18f, ink, stroke);
                    for (int row = 0; row < 2; row++)
                    {
                        for (int key = 0; key < 5; key++)
                            Dot(x + w * (0.21f + key * 0.135f + row * 0.04f), y + h * (0.32f + row * 0.20f),
                                stroke * 0.48f, ink);
                    }
                    Line(x + w * 0.29f, y + h * 0.74f, x + w * 0.71f, y + h * 0.74f, ink, stroke);
                    break;
                case DeviceKind.Headset:
                    float earWidth = w * 0.25f;
                    float bandY = y + h * 0.52f;
                    float bandRadius = MathF.Min((w - earWidth) / 2f, bandY - y - half);
                    Arc(x + w / 2f, bandY, bandRadius, MathF.PI, 2f * MathF.PI, ink, stroke);
                    Fill(x, bandY, earWidth, h * 0.48f, earWidth * 0.42f, ink);
                    Fill(x + w - earWidth, bandY, earWidth, h * 0.48f, earWidth * 0.42f, ink);
                    break;
                default:
                    Stroke(x + half, y + half, w - stroke, h - stroke, h * 0.24f, ink, stroke);
                    Dot(x + w * 0.33f, y + h * 0.36f, stroke * 0.75f, ink);
                    Line(x + w * 0.33f, y + h * 0.68f, x + w * 0.67f, y + h * 0.68f, ink, stroke);
                    break;
            }
        }
        finally
        {
            PopClip();
        }
    }

    public float Text(string text, float x, float y, float w, float h, float size, Color color, int weight = 400,
        bool number = false, DWRITE_TEXT_ALIGNMENT align = DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING,
        bool ellipsis = false)
    {
        if (text.Length == 0)
            return 0;
        var layout = fonts.Layout(text, size, weight, number, w, h, align, ellipsis);
        try
        {
            target->DrawTextLayout(Graphics.Point(x, y), layout, Brush(color), D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_CLIP);
            layout->GetMetrics(out var metrics);
            return metrics.widthIncludingTrailingWhitespace;
        }
        finally
        {
            Graphics.Release(layout);
        }
    }

    public float Measure(string text, float size, int weight = 400, bool number = false)
    {
        if (text.Length == 0)
            return 0;
        var layout = fonts.Layout(text, size, weight, number, 1024, 96,
            DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING, false);
        try
        {
            layout->GetMetrics(out var metrics);
            return metrics.widthIncludingTrailingWhitespace;
        }
        finally
        {
            Graphics.Release(layout);
        }
    }

    public float TextWrapped(string text, float x, float y, float w, float h, float size, Color color, int weight = 400,
        DWRITE_TEXT_ALIGNMENT align = DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING)
    {
        if (text.Length == 0 || w <= 0 || h <= 0)
            return 0;
        var layout = fonts.Layout(text, size, weight, number: false, w, h, align, ellipsis: false, wrap: true);
        try
        {
            target->DrawTextLayout(Graphics.Point(x, y), layout, Brush(color), D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_CLIP);
            layout->GetMetrics(out var metrics);
            return metrics.height;
        }
        finally
        {
            Graphics.Release(layout);
        }
    }

    public float MeasureWrappedHeight(string text, float w, float size, int weight = 400)
    {
        if (text.Length == 0 || w <= 0)
            return 0;
        var layout = fonts.Layout(text, size, weight, number: false, w, 4096,
            DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING, ellipsis: false, wrap: true);
        try
        {
            layout->GetMetrics(out var metrics);
            return metrics.height;
        }
        finally
        {
            Graphics.Release(layout);
        }
    }

    public void Dispose()
    {
        Graphics.Release(brush);
        brush = null;
    }
}
