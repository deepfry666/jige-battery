using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.System.Com;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class Composition : IDisposable
{
    private IDCompositionDesktopDevice* device;
    private IDCompositionTarget* target;
    private IDXGIDevice3* dxgi3;
    private TextFactory? fonts;

    public Composition(HWND hwnd, float scale)
    {
        Scale = scale;
        ID3D11Device* d3d = null;
        IDXGIDevice* dxgi = null;
        ID2D1Device* d2d = null;
        try
        {
            var flags = D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT;
            var status = PInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, default,
                flags, null, 0, 7, &d3d, null, null);
            if (status.Failed)
                status = PInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP, default,
                    flags, null, 0, 7, &d3d, null, null);
            Graphics.Check(status, "创建设备图形上下文");

            var dxgiId = IDXGIDevice.IID_Guid;
            Graphics.Check(d3d->QueryInterface(&dxgiId, (void**)&dxgi), "获取 DXGI 设备");
            var dxgi3Id = IDXGIDevice3.IID_Guid;
            IDXGIDevice3* trimDevice = null;
            if (d3d->QueryInterface(&dxgi3Id, (void**)&trimDevice).Succeeded)
                dxgi3 = trimDevice;
            Graphics.Check(PInvoke.D2D1CreateDevice(dxgi, null, &d2d), "创建 Direct2D 设备");
            var desktopId = IDCompositionDesktopDevice.IID_Guid;
            IDCompositionDesktopDevice* desktop = null;
            Graphics.Check(PInvoke.DCompositionCreateDevice2((IUnknown*)d2d, &desktopId, (void**)&desktop),
                "创建 DirectComposition 设备");
            device = desktop;
            IDCompositionTarget* destination = null;
            // 合成树在原生子窗口下面，行内 EDIT 可以自行绘制文字与光标。
            device->CreateTargetForHwnd(hwnd, false, &destination);
            if (destination == null)
                throw new InvalidOperationException("窗口合成目标为空");
            target = destination;
            fonts = new TextFactory();
            Root = new(this, null, 0, 0, 0, 0);
            target->SetRoot((IDCompositionVisual*)Root.Visual);
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            Graphics.Release(d2d);
            Graphics.Release(dxgi);
            Graphics.Release(d3d);
        }
    }

    private float Scale { get; }
    public Layer Root { get; } = null!;

    private int Pixels(float value) => (int)MathF.Round(value * Scale, MidpointRounding.AwayFromZero);

    public void Commit()
    {
        if (device == null)
            throw new ObjectDisposedException(nameof(Composition));
        device->Commit();
    }

    public void Trim()
    {
        if (dxgi3 != null)
            dxgi3->Trim();
    }

    public void Dispose()
    {
        Root?.Dispose();
        fonts?.Dispose();
        Graphics.Release(target);
        target = null;
        Graphics.Release(device);
        device = null;
        if (dxgi3 != null)
        {
            dxgi3->Trim();
            Graphics.Release(dxgi3);
            dxgi3 = null;
        }
    }

    internal sealed class Layer : IDisposable
    {
        private readonly Composition composition;
        private readonly Layer? parent;
        private readonly int baseY;
        private bool attached;
        private IDCompositionSurface* surface;
        private IDCompositionEffectGroup* opacity;
        public IDCompositionVisual2* Visual { get; private set; }

        public Layer(Composition composition, Layer? parent, float x, float y, float width, float height)
        {
            this.composition = composition;
            this.parent = parent;
            baseY = composition.Pixels(y);
            IDCompositionVisual2* visual = null;
            composition.device->CreateVisual(&visual);
            if (visual == null)
                throw new InvalidOperationException("合成图层为空");
            Visual = visual;
            try
            {
                Visual->SetOffsetX(composition.Pixels(x));
                Visual->SetOffsetY(baseY);
                if (width > 0 && height > 0)
                {
                    IDCompositionSurface* bitmap = null;
                    composition.device->CreateSurface((uint)composition.Pixels(width), (uint)composition.Pixels(height),
                        DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_PREMULTIPLIED, &bitmap);
                    if (bitmap == null)
                        throw new InvalidOperationException("图层表面为空");
                    surface = bitmap;
                    Visual->SetContent((IUnknown*)surface);
                }
                // 参考图层为 null 时，insertAbove=true 会把新图层放到同级最底层（DirectComposition 的约定），
                // 0.1.2 因此把设备行和刷新按钮压在半透明面板背景下面，整行发灰。false 才是放到最顶层。
                if (parent is not null)
                {
                    parent.Visual->AddVisual((IDCompositionVisual*)Visual, false, null);
                    attached = true;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Draw(Action<Canvas> draw)
        {
            if (surface == null)
                throw new InvalidOperationException("图层表面不可用");
            var iid = ID2D1DeviceContext.IID_Guid;
            // 请求 Direct2D 上下文时由 DirectComposition 配对 BeginDraw 和 EndDraw。
            surface->BeginDraw(null, in iid, out var drawing, out var offset);
            if (drawing == null)
                throw new InvalidOperationException("Direct2D 绘制上下文为空");
            var render = (ID2D1RenderTarget*)drawing;
            bool threw = false;
            try
            {
                render->SetDpi(96, 96);
                D2D_MATRIX_3X2_F matrix = default;
                matrix._11 = matrix._22 = composition.Scale;
                matrix._31 = offset.X;
                matrix._32 = offset.Y;
                render->SetTransform(in matrix);
                render->Clear((D2D1_COLOR_F*)null);
                using var canvas = new Canvas(render, composition.fonts!);
                draw(canvas);
            }
            catch
            {
                threw = true;
                throw;
            }
            finally
            {
                Graphics.Release(render);
                if (threw)
                {
                    // 避免 EndDraw 的 COMException 吞掉 draw(canvas) 抛出的原始异常。
                    try { surface->EndDraw(); }
                    catch { }
                }
                else
                {
                    surface->EndDraw();
                }
            }
        }

        public void Clip(float width, float height) =>
            Visual->SetClip(Graphics.Rect(0, 0, composition.Pixels(width), composition.Pixels(height)));

        public void SetY(float y) => Visual->SetOffsetY(composition.Pixels(y));

        public void SetX(float x) => Visual->SetOffsetX(x * composition.Scale);

        public void AnimateX(float from, float to, int milliseconds)
        {
            var animation = composition.Animate(from * composition.Scale, to * composition.Scale, 0,
                milliseconds / 1000.0, Ease.Enter);
            try
            {
                Visual->SetOffsetX(animation);
            }
            finally
            {
                Graphics.Release(animation);
            }
        }

        public void PageMotion(float fromX, float toX, float fromOpacity, float toOpacity)
        {
            AnimateX(fromX, toX, PanelLayout.TransitionMilliseconds);
            Fade(fromOpacity, toOpacity, 0, PanelLayout.TransitionMilliseconds / 1000.0, Ease.Enter);
        }

        public void ResetPageMotion()
        {
            SetX(0);
            if (opacity != null)
                opacity->SetOpacity(1f);
        }

        private IDCompositionEffectGroup* Opacity()
        {
            if (opacity == null)
            {
                IDCompositionEffectGroup* group = null;
                composition.device->CreateEffectGroup(&group);
                if (group == null)
                    throw new InvalidOperationException("图层透明效果为空");
                opacity = group;
                Visual->SetEffect((IDCompositionEffect*)group);
            }
            return opacity;
        }

        public void Open(double delay = 0, bool row = false)
        {
            MoveY(row ? 6 : 16, 0, delay, row ? .30 : .25, Ease.Enter);
            Fade(0, 1, delay, row ? .20 : .25, row ? Ease.Standard : Ease.Enter);
        }

        public void SlideIn(float distance)
        {
            var animation = composition.Animate(composition.Pixels(distance), 0, 0, .35, Ease.Enter);
            try
            {
                Visual->SetOffsetX(animation);
            }
            finally
            {
                Graphics.Release(animation);
            }
        }

        public void Close()
        {
            MoveY(0, 16, 0, .12, Ease.Exit);
            Fade(1, 0, 0, .12, Ease.Exit);
        }

        public void FadeIn(double durationSeconds = .14) =>
            Fade(0, 1, 0, durationSeconds, Ease.Standard);

        public void FadeOut(double durationSeconds = .13) =>
            Fade(1, 0, 0, durationSeconds, Ease.Exit);

        public void SetOpacity(float value)
        {
            if (opacity != null)
                opacity->SetOpacity(value);
        }

        private void MoveY(float from, float to, double delay, double duration, Ease ease)
        {
            var animation = composition.Animate(baseY + composition.Pixels(from), baseY + composition.Pixels(to),
                delay, duration, ease);
            try
            {
                Visual->SetOffsetY(animation);
            }
            finally
            {
                Graphics.Release(animation);
            }
        }

        private void Fade(float from, float to, double delay, double duration, Ease ease)
        {
            var animation = composition.Animate(from, to, delay, duration, ease);
            try
            {
                Opacity()->SetOpacity(animation);
            }
            finally
            {
                Graphics.Release(animation);
            }
        }

        public void Breathe()
        {
            IDCompositionAnimation* animation = null;
            composition.device->CreateAnimation(&animation);
            try
            {
                AddCurve(animation, 0, .8, .15f, .85f, Ease.InOut);
                AddCurve(animation, .8, .8, .85f, .15f, Ease.InOut);
                animation->AddRepeat(1.6, 1.6);
                Opacity()->SetOpacity(animation);
            }
            finally
            {
                Graphics.Release(animation);
            }
        }

        public void Spin(float center)
        {
            var animation = composition.Animate(0, 360, 0, .60, Ease.Spin);
            IDCompositionRotateTransform* rotation = null;
            try
            {
                composition.device->CreateRotateTransform(&rotation);
                rotation->SetCenterX(composition.Pixels(center));
                rotation->SetCenterY(composition.Pixels(center));
                rotation->SetAngle(animation);
                Visual->SetTransform((IDCompositionTransform*)rotation);
            }
            finally
            {
                Graphics.Release(rotation);
                Graphics.Release(animation);
            }
        }

        public void Dispose()
        {
            if (Visual == null && surface == null && opacity == null)
                return;
            if (attached && parent is not null && parent.Visual != null && Visual != null)
            {
                try
                {
                    parent.Visual->RemoveVisual((IDCompositionVisual*)Visual);
                }
                catch (Exception error)
                {
                    Desktop.Log("释放图层失败", error);
                }
            }
            attached = false;
            Graphics.Release(opacity);
            opacity = null;
            Graphics.Release(surface);
            surface = null;
            Graphics.Release(Visual);
            Visual = null;
        }
    }

    private readonly record struct Ease(double X1, double Y1, double X2, double Y2)
    {
        public static Ease Enter => new(PanelLayout.EaseX1, PanelLayout.EaseY1, PanelLayout.EaseX2, PanelLayout.EaseY2);
        public static Ease Exit => new(.7, 0, 1, .5);
        public static Ease Standard => new(.25, .1, .25, 1);
        public static Ease InOut => new(.42, 0, .58, 1);
        public static Ease Spin => new(.4, 0, .2, 1);
    }

    private IDCompositionAnimation* Animate(float from, float to, double delay, double duration, Ease ease)
    {
        IDCompositionAnimation* animation = null;
        device->CreateAnimation(&animation);
        try
        {
            if (delay > 0)
                animation->AddCubic(0, from, 0, 0, 0);
            AddCurve(animation, delay, duration, from, to, ease);
            animation->End(delay + duration, to);
            return animation;
        }
        catch
        {
            Graphics.Release(animation);
            throw;
        }
    }

    private static void AddCurve(IDCompositionAnimation* animation, double start, double duration,
        float from, float to, Ease ease)
    {
        // 按贝塞尔曲线的参数取样，换算为合成器使用的分段时间多项式。
        const int segments = 12;
        double lastTime = start, lastValue = from;
        double slope = Slope(0);
        for (int i = 1; i <= segments; i++)
        {
            double t = (double)i / segments;
            double time = start + duration * PanelLayout.Bezier(t, ease.X1, ease.X2);
            double value = from + (to - from) * PanelLayout.Bezier(t, ease.Y1, ease.Y2);
            double seconds = time - lastTime;
            double rate = (value - lastValue) / seconds;
            double nextSlope = Slope(t, rate);
            animation->AddCubic(lastTime, (float)lastValue, (float)slope,
                (float)((3 * rate - 2 * slope - nextSlope) / seconds),
                (float)((slope + nextSlope - 2 * rate) / (seconds * seconds)));
            lastTime = time;
            lastValue = value;
            slope = nextSlope;
        }

        double Slope(double t, double fallback = 0)
        {
            double dx = Derivative(t, ease.X1, ease.X2);
            return dx > 0 ? (to - from) * Derivative(t, ease.Y1, ease.Y2) / (duration * dx) : fallback;
        }
    }

    private static double Derivative(double t, double a, double b) =>
        3 * ((1 - t) * (1 - t) * a + 2 * (1 - t) * t * (b - a) + t * t * (1 - b));
}
