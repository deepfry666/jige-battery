using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.UI.WindowsAndMessaging.WINDOW_STYLE;

namespace Jige.Tray.Windows;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class InlineEdit
{
    public const uint CompletedMessage = 0x8011;
    private readonly HWND owner;
    private readonly Palette colors;
    private HFONT font;
    private HBRUSH background;
    private GCHandle state;
    private bool composing;
    public bool? RequestedSave { get; private set; }

    public InlineEdit(HWND owner, string deviceId, string name, RECT bounds, int fontSize, Palette colors)
    {
        this.owner = owner;
        this.colors = colors;
        DeviceId = deviceId;
        try
        {
            fixed (char* edit = "EDIT", title = name)
            {
                Handle = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_LAYERED, new PCWSTR(edit), new PCWSTR(title),
                    WS_CHILD | (WINDOW_STYLE)0x0080, // ES_AUTOHSCROLL
                    bounds.left, bounds.top, bounds.Width, bounds.Height,
                    owner, default, (HINSTANCE)PInvoke.GetModuleHandle(default(PCWSTR)), null);
            }
            // 父窗口没有 GDI 重定向位图；分层子窗口为原生编辑控件提供独立的绘制表面。
            if (Handle.IsNull || !PInvoke.SetLayeredWindowAttributes(Handle, default, 255,
                LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法创建改名输入框");

            fixed (char* face = "Segoe UI Variable Text")
                font = PInvoke.CreateFont(-Math.Max(1, fontSize), 0, 0, 0, 400, 0, 0, 0,
                    FONT_CHARSET.DEFAULT_CHARSET, 0, 0, FONT_QUALITY.ANTIALIASED_QUALITY, 0, new PCWSTR(face));
            background = PInvoke.CreateSolidBrush(ColorRef(colors.Solid));
            if (font.IsNull || background.IsNull)
                throw new InvalidOperationException("无法创建改名输入框样式");

            state = GCHandle.Alloc(this);
            if (!PInvoke.SetWindowSubclass(Handle, &EditProcedure, 1, (nuint)GCHandle.ToIntPtr(state)))
                throw new InvalidOperationException("无法注册改名输入框消息处理器");
            PInvoke.SendMessage(Handle, 0x0030, (WPARAM)(nuint)font.Value, default); // WM_SETFONT
            PInvoke.SendMessage(Handle, 0x00D3, (WPARAM)3, (LPARAM)(2 | (2 << 16))); // EM_SETMARGINS
            PInvoke.SendMessage(Handle, 0x00C5, (WPARAM)(nuint)Math.Max(256, name.Length), default); // EM_SETLIMITTEXT
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public HWND Handle { get; private set; }
    public string DeviceId { get; }

    public void Focus()
    {
        PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_SHOW);
        PInvoke.SetFocus(Handle);
        PInvoke.SendMessage(Handle, 0x00B1, default, (LPARAM)(-1)); // EM_SETSEL
    }

    public string Text()
    {
        int length = (int)PInvoke.SendMessage(Handle, 0x000E, default, default).Value; // WM_GETTEXTLENGTH
        var buffer = new char[length + 1];
        fixed (char* text = buffer)
            return new string(buffer, 0, PInvoke.GetWindowText(Handle, new PWSTR(text), buffer.Length));
    }

    public LRESULT PaintColors(HDC dc)
    {
        PInvoke.SetTextColor(dc, ColorRef(colors.Ink));
        PInvoke.SetBkColor(dc, ColorRef(colors.Solid));
        return (LRESULT)(nint)background.Value;
    }

    private static COLORREF ColorRef(Color color) =>
        new(((color.Rgb >> 16) & 255) | (color.Rgb & 0xff00) | ((color.Rgb & 255) << 16));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT EditProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam, nuint subclass, nuint data)
    {
        var editor = (InlineEdit)GCHandle.FromIntPtr((nint)data).Target!;
        switch (message)
        {
            // 输入法组合期间由原生 EDIT 处理候选确认、取消和光标定位。
            case 0x010D: editor.composing = true; break; // WM_IME_STARTCOMPOSITION
            case 0x010E: editor.composing = false; break; // WM_IME_ENDCOMPOSITION
            case 0x0100 when !editor.composing && wParam.Value is 0x0D or 0x1B:
                editor.PostFinish(save: wParam.Value == 0x0D);
                return default;
            case 0x0102 when wParam.Value is 0x0D or 0x1B: // WM_CHAR
                return default;
            case 0x0008: // WM_KILLFOCUS
                var result = PInvoke.DefSubclassProc(window, message, wParam, lParam);
                editor.PostFinish(save: true);
                return result;
        }
        return PInvoke.DefSubclassProc(window, message, wParam, lParam);
    }

    private void PostFinish(bool save)
    {
        if (RequestedSave is null &&
            PInvoke.PostMessage(owner, CompletedMessage, (WPARAM)(save ? 1u : 0u), (LPARAM)(nint)Handle.Value))
            RequestedSave = save;
    }

    public void ReturnFocus()
    {
        if (PInvoke.GetForegroundWindow() == owner && PInvoke.GetFocus() == Handle)
            PInvoke.SetFocus(owner);
    }

    public void Dispose()
    {
        if (!Handle.IsNull)
        {
            PInvoke.RemoveWindowSubclass(Handle, &EditProcedure, 1);
            PInvoke.DestroyWindow(Handle);
            Handle = default;
        }
        if (state.IsAllocated)
            state.Free();
        if (!font.IsNull)
            PInvoke.DeleteObject(font);
        if (!background.IsNull)
            PInvoke.DeleteObject(background);
    }
}
