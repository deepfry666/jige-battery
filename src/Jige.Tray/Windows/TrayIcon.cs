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
internal sealed unsafe class TrayIcon(HWND window, uint callback, uint id = 1) : IDisposable
{
    // D2（#14）：复用主 STA 线程上的 WIC/D2D/DirectWrite 工厂以及按尺寸缓存的位图与渲染目标，
    // 高 DPI 更新托盘图标时不再每次重建图形工厂和渲染表面。
    private sealed class ScalableFactories : IDisposable
    {
        private static readonly Guid BgraPixelFormat = new("6fddc324-4e03-4bfe-b185-3d77768dc910");

        private sealed class SurfaceSlot(IWICBitmap* bitmap, ID2D1RenderTarget* renderTarget, Canvas canvas) : IDisposable
        {
            public IWICBitmap* Bitmap { get; private set; } = bitmap;
            public ID2D1RenderTarget* RenderTarget { get; private set; } = renderTarget;
            public Canvas Canvas { get; } = canvas;

            public void Dispose()
            {
                Canvas.Dispose();
                Graphics.Release(RenderTarget);
                RenderTarget = null;
                Graphics.Release(Bitmap);
                Bitmap = null;
            }
        }

        private const int MaxCachedSizes = 4;
        public IWICImagingFactory* Wic;
        public ID2D1Factory* D2D;
        public TextFactory Fonts;
        private readonly Dictionary<int, SurfaceSlot> surfaces = [];

        public ScalableFactories()
        {
            var wicClass = new Guid("cacaf262-9370-4615-a13b-9f5539da4c0a");
            var wicId = IWICImagingFactory.IID_Guid;
            var d2dId = ID2D1Factory.IID_Guid;
            IWICImagingFactory* wic = null;
            ID2D1Factory* d2d = null;
            try
            {
                Graphics.Check(PInvoke.CoCreateInstance(&wicClass, null, CLSCTX.CLSCTX_INPROC_SERVER, &wicId, (void**)&wic),
                    "创建图标位图工厂");
                Graphics.Check(PInvoke.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED,
                    &d2dId, null, (void**)&d2d), "创建 Direct2D 图标工厂");
                Wic = wic;
                D2D = d2d;
                Fonts = new TextFactory();
            }
            catch
            {
                Graphics.Release(d2d);
                Graphics.Release(wic);
                throw;
            }
        }

        public void AcquireSurface(int size, out IWICBitmap* outBitmap, out ID2D1RenderTarget* outTarget, out Canvas outCanvas)
        {
            if (surfaces.TryGetValue(size, out var cached))
            {
                outBitmap = cached.Bitmap;
                outTarget = cached.RenderTarget;
                outCanvas = cached.Canvas;
                return;
            }

            if (surfaces.Count >= MaxCachedSizes)
                ReleaseSurfaces();

            IWICBitmap* nextBitmap = null;
            ID2D1RenderTarget* nextTarget = null;
            try
            {
                Wic->CreateBitmap((uint)size, (uint)size, BgraPixelFormat,
                    WICBitmapCreateCacheOption.WICBitmapCacheOnLoad, &nextBitmap);
                var properties = new D2D1_RENDER_TARGET_PROPERTIES
                {
                    dpiX = 96, dpiY = 96,
                    pixelFormat = new()
                    {
                        format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                        alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED
                    }
                };
                D2D->CreateWicBitmapRenderTarget(nextBitmap, in properties, &nextTarget);
                var nextCanvas = new Canvas(nextTarget, Fonts);
                var slot = new SurfaceSlot(nextBitmap, nextTarget, nextCanvas);
                surfaces[size] = slot;
                outBitmap = slot.Bitmap;
                outTarget = slot.RenderTarget;
                outCanvas = slot.Canvas;
            }
            catch
            {
                Graphics.Release(nextTarget);
                Graphics.Release(nextBitmap);
                throw;
            }
        }

        public void InvalidateSurface() => ReleaseSurfaces();

        private void ReleaseSurfaces()
        {
            foreach (var slot in surfaces.Values)
                slot.Dispose();
            surfaces.Clear();
        }

        public void Dispose()
        {
            ReleaseSurfaces();
            Fonts.Dispose();
            Graphics.Release(D2D);
            D2D = null;
            Graphics.Release(Wic);
            Wic = null;
        }
    }

    [ThreadStatic]
    private static ScalableFactories? sharedFactories;

    private HICON icon;
    private IconAppearance? appearance;
    private int size;
    private string? tip;
    private bool registered;

    public uint Id => id;
    public bool Registered => registered;

    public static void DisposeSharedRenderer()
    {
        sharedFactories?.Dispose();
        sharedFactories = null;
    }

    public bool TryGetRect(out RECT rectangle)
    {
        var identity = new NOTIFYICONIDENTIFIER { cbSize = (uint)sizeof(NOTIFYICONIDENTIFIER), hWnd = window, uID = id };
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
        // H1：使用 NOTIFYICON_VERSION_4 且不设 NIF_SHOWTIP，关闭系统默认文字提示，改由 NIN_POPUPOPEN 自绘悬停卡片；
        // szTip 仍继续填写，供读屏软件等辅助功能读取。
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_TIP;
        if (changed || !registered)
            data.uFlags |= NOTIFY_ICON_DATA_FLAGS.NIF_ICON;
        data.hIcon = newIcon;
        data.uCallbackMessage = callback;
        // NOTIFYICONDATAW 的提示缓冲区在结构内，包含末尾空字符。
        char* destination = (char*)&data.szTip;
        nextTip.AsSpan(0, Math.Min(nextTip.Length, 127)).CopyTo(new Span<char>(destination, 127));

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

        if (registered)
        {
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
        else if (changed && !newIcon.IsNull)
        {
            // Shell_NotifyIcon 失败时销毁新图标并保留旧图标句柄与状态，避免失败窗口内旧句柄被提前销毁。
            PInvoke.DestroyIcon(newIcon);
        }
    }

    private NOTIFYICONDATAW Data() => new() { cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = window, uID = id };

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
                PixelTrayIcon.Draw(pixels, appearance);
            else
                DrawScalable(MemoryMarshal.AsBytes(pixels), size, appearance);

            if (pixels.IndexOfAnyExcept(0u) < 0)
                throw new InvalidOperationException("托盘图标为空");
            PixelTrayIcon.ToStraightAlpha(pixels);

            int stride = ((size + 15) / 16) * 2;
            Span<byte> maskBits = stackalloc byte[stride * size];
            maskBits.Clear();
            for (int y = 0; y < size; y++)
            {
                int maskRow = y * stride;
                for (int x = 0; x < size; x++)
                {
                    if ((pixels[y * size + x] >> 24) == 0)
                        maskBits[maskRow + x / 8] |= (byte)(0x80 >> (x % 8));
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
        for (int attempt = 0; ; attempt++)
        {
            sharedFactories ??= new ScalableFactories();
            sharedFactories.AcquireSurface(size, out var image, out var render, out var canvas);
            try
            {
                render->BeginDraw();
                bool threw = false;
                try
                {
                    render->Clear((D2D1_COLOR_F*)null);
                    if (icon.Kind is { } kind)
                    {
                        var layout = TrayIconLayout.Compute(size, kind, icon.Percentage);
                        canvas.TrayDeviceGlyph(sharedFactories.D2D, kind, layout, icon);
                    }
                    else
                    {
                        canvas.Text("–", 0, -size * .07f, size, size * 1.06f, size * .90f, icon.FillInk,
                            700, true, DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER);
                    }
                }
                catch
                {
                    threw = true;
                    throw;
                }
                finally
                {
                    var end = render->EndDraw();
                    if (!threw)
                        Graphics.Check(end, "绘制托盘图标");
                }
                image->CopyPixels(null, (uint)(size * 4), pixels);
                return;
            }
            catch (Exception error)
            {
                sharedFactories.InvalidateSurface();
                if (attempt == 0 && Graphics.IsDeviceLost(error))
                {
                    DisposeSharedRenderer();
                    continue;
                }
                throw;
            }
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
