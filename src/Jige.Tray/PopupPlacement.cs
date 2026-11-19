namespace Jige.Tray;

internal readonly record struct Bounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;
}

internal static class PopupPlacement
{
    public static Bounds Place(Bounds icon, Bounds monitor, Bounds work, int width, int height, int gap, bool notice)
    {
        int x = icon.CenterX - width / 2;
        int y = icon.CenterY - height / 2;
        if (notice)
        {
            x = work.Right - width - gap;
            y = work.Bottom - height - gap;
        }
        else
        {
            // 任务栏可在任意一边。托盘折叠面板位于工作区内时，使用任务栏所在边。
            int top = work.Top > monitor.Top ? Math.Abs(icon.CenterY - monitor.Top) : int.MaxValue;
            int bottom = work.Bottom < monitor.Bottom ? Math.Abs(monitor.Bottom - icon.CenterY) : int.MaxValue;
            int left = work.Left > monitor.Left ? Math.Abs(icon.CenterX - monitor.Left) : int.MaxValue;
            int right = work.Right < monitor.Right ? Math.Abs(monitor.Right - icon.CenterX) : int.MaxValue;
            if (Math.Min(Math.Min(top, bottom), Math.Min(left, right)) == int.MaxValue)
            {
                top = Math.Abs(icon.CenterY - monitor.Top);
                bottom = Math.Abs(monitor.Bottom - icon.CenterY);
                left = Math.Abs(icon.CenterX - monitor.Left);
                right = Math.Abs(monitor.Right - icon.CenterX);
            }

            int nearest = Math.Min(Math.Min(top, bottom), Math.Min(left, right));
            if (nearest == top)
                y = Math.Max(icon.Bottom, work.Top) + gap;
            else if (nearest == bottom)
                y = Math.Min(icon.Top, work.Bottom) - height - gap;
            else if (nearest == left)
                x = Math.Max(icon.Right, work.Left) + gap;
            else
                x = Math.Min(icon.Left, work.Right) - width - gap;
        }

        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        return new(x, y, x + width, y + height);
    }
}
