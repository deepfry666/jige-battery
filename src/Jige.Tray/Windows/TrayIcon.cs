using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class TrayIcon(HWND window, uint callback) : IDisposable
{
    private const uint Id = 1;
    private HICON icon;
    private IconAppearance? appearance;
    private int size;
    private string? tip;
    private bool registered;
    public bool Registered => registered;

    public bool TryGetRect(out RECT rectangle)
    {
        var identity = new NOTIFYICONIDENTIFIER { cbSize = (uint)sizeof(NOTIFYICONIDENTIFIER), hWnd = window, uID = Id };
        RECT bounds;
        var result = PInvoke.Shell_NotifyIconGetRect(&identity, &bounds);
        rectangle = bounds;
        return result.Succeeded && bounds.right > bounds.left && bounds.bottom > bounds.top;
    }

    public void Recover() => registered = false;

    public void Update(IconAppearance next, int nextSize, string nextTip)
    {
        bool changed = icon.IsNull || appearance != next || size != nextSize;
        if (registered && !changed && nextTip == tip)
            return;

        HICON newIcon = changed ? Draw(nextSize, next) : icon;
        var data = Data();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_TIP;
        if (changed || !registered)
            data.uFlags |= NOTIFY_ICON_DATA_FLAGS.NIF_ICON;
        if (nextTip.Length > 0)
            data.uFlags |= NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.hIcon = newIcon;
        data.uCallbackMessage = callback;
        // NOTIFYICONDATAW 的提示缓冲区在结构内，包含末尾空字符。
        char* destination = (char*)&data.szTip;
        nextTip.AsSpan().CopyTo(new Span<char>(destination, 127));

        bool wasRegistered = registered;
        registered = PInvoke.Shell_NotifyIcon(wasRegistered ? NOTIFY_ICON_MESSAGE.NIM_MODIFY : NOTIFY_ICON_MESSAGE.NIM_ADD, &data);
        if (registered && !wasRegistered)
        {
            data.Anonymous.uVersion = 4;
            if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, &data))
            {
                PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
                registered = false;
            }
        }

        if (changed)
        {
            if (!icon.IsNull)
                PInvoke.DestroyIcon(icon);
            icon = newIcon;
            appearance = next;
            size = nextSize;
        }
        tip = nextTip;
    }

    private NOTIFYICONDATAW Data() => new() { cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = window, uID = Id };

    public static HICON Draw(int size, IconAppearance appearance)
    {
        var bitmapInfo = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = size,
                biHeight = -size,
                biPlanes = 1,
                biBitCount = 32
            }
        };
        void* bits = null;
        var bitmap = PInvoke.CreateDIBSection(default, &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS, &bits, default, 0);
        if (bitmap.IsNull || bits == null)
        {
            if (!bitmap.IsNull)
                PInvoke.DeleteObject(bitmap);
            throw new InvalidOperationException("无法分配托盘图标位图");
        }

        HBITMAP mask = default;
        try
        {
            var pixels = new Span<uint>(bits, size * size);
            pixels.Clear();
            if (size == 16)
                PixelDigits.Draw(pixels, appearance);
            else
                DrawScalable(MemoryMarshal.AsBytes(pixels), size, appearance);

            if (pixels.IndexOfAnyExcept(0u) < 0)
                throw new InvalidOperationException("托盘图标为空");

            int stride = ((size + 15) / 16) * 2;
            Span<byte> maskBits = stackalloc byte[stride * size];
            maskBits.Clear();
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if ((pixels[y * size + x] >> 24) == 0)
                        maskBits[y * stride + x / 8] |= (byte)(0x80 >> (x % 8));
                }
            }

            fixed (byte* pattern = maskBits)
                mask = PInvoke.CreateBitmap(size, size, 1, 1, pattern);
            if (mask.IsNull)
                throw new InvalidOperationException("无法分配托盘图标掩码");
            var details = new ICONINFO { fIcon = true, hbmColor = bitmap, hbmMask = mask };
            var result = PInvoke.CreateIconIndirect(&details);
            if (result.IsNull)
                throw new InvalidOperationException("无法创建托盘图标句柄");
            return result;
        }
        finally
        {
            if (!mask.IsNull)
                PInvoke.DeleteObject(mask);
            PInvoke.DeleteObject(bitmap);
        }
    }

    private static void DrawScalable(Span<byte> pixels, int size, IconAppearance icon)
    {
        var wicClass = new Guid("cacaf262-9370-4615-a13b-9f5539da4c0a");
        var wicId = IWICImagingFactory.IID_Guid;
        var pixelFormat = new Guid("6fddc324-4e03-4bfe-b185-3d77768dc910");
        IWICImagingFactory* wic = null;
        IWICBitmap* image = null;
        ID2D1Factory* factory = null;
        ID2D1RenderTarget* render = null;
        try
        {
            Graphics.Check(PInvoke.CoCreateInstance(&wicClass, null, CLSCTX.CLSCTX_INPROC_SERVER, &wicId, (void**)&wic),
                "创建图标位图工厂");
            wic->CreateBitmap((uint)size, (uint)size, pixelFormat, WICBitmapCreateCacheOption.WICBitmapCacheOnLoad, &image);
            var d2dId = ID2D1Factory.IID_Guid;
            Graphics.Check(PInvoke.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED,
                &d2dId, null, (void**)&factory), "创建 Direct2D 图标工厂");
            var properties = new D2D1_RENDER_TARGET_PROPERTIES
            {
                dpiX = 96, dpiY = 96,
                pixelFormat = new()
                {
                    format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                    alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED
                }
            };
            factory->CreateWicBitmapRenderTarget(image, in properties, &render);
            using var fonts = new TextFactory();
            using var canvas = new Canvas(render, fonts);
            render->BeginDraw();
            try
            {
                render->Clear((D2D1_COLOR_F*)null);
                if (icon.Full)
                    canvas.Battery(0, 0, size / 16f, icon.Ink);
                else
                {
                    string value = icon.Percentage?.ToString() ?? "–";
                    float fontSize = size * (value.Length == 2 ? .78f : .90f);
                    canvas.Text(value, 0, -size * .07f, size, size * (icon.Underline ? .92f : 1.06f),
                        fontSize, icon.Ink, 700, true, DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER);
                    if (icon.Underline)
                        canvas.Fill(size * .16f, size * .86f, size * .68f, size * .1f, size * .05f, icon.Ink);
                }
            }
            finally
            {
                Graphics.Check(render->EndDraw(), "绘制托盘图标");
            }
            image->CopyPixels(null, (uint)(size * 4), pixels);
        }
        finally
        {
            Graphics.Release(render);
            Graphics.Release(factory);
            Graphics.Release(image);
            Graphics.Release(wic);
        }
    }

    public void Dispose()
    {
        if (registered)
        {
            var data = Data();
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
            registered = false;
        }
        if (!icon.IsNull)
            PInvoke.DestroyIcon(icon);
        icon = default;
    }
}
