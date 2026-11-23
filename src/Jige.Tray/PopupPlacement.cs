namespace Jige.Tray;

internal readonly record struct Bounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;
}

internal enum TaskbarEdge { Left = 0, Top = 1, Right = 2, Bottom = 3 }

internal static class PopupPlacement
{
    public static Bounds Place(Bounds icon, Bounds monitor, Bounds work, int width, int height, int gap, bool notice,
        TaskbarEdge? taskbarEdge = null)
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
            var edge = ResolveEdge(icon, monitor, work, taskbarEdge);
            if (edge == TaskbarEdge.Top)
                y = Math.Max(icon.Bottom, work.Top) + gap;
            else if (edge == TaskbarEdge.Bottom)
                y = Math.Min(icon.Top, work.Bottom) - height - gap;
            else
            {
                x = edge == TaskbarEdge.Left ? Math.Max(icon.Right, work.Left) + gap : Math.Min(icon.Left, work.Right) - width - gap;
                y = Math.Min(icon.Bottom, work.Bottom) - height;
            }
        }

        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        return new(x, y, x + width, y + height);
    }

    internal static TaskbarEdge ResolveEdge(Bounds icon, Bounds monitor, Bounds work, TaskbarEdge? knownEdge)
    {
        // 自动隐藏任务栏下 work == monitor（或仅异侧存在第三方停靠 AppBar）；
        // 若调用方提供了 ABM_GETTASKBARPOS 的真实停靠边且图标紧邻该边，直接采用避免误判为 AppBar 或右边缘。
        int top = work.Top > monitor.Top ? Math.Abs(icon.CenterY - monitor.Top) : int.MaxValue;
        int bottom = work.Bottom < monitor.Bottom ? Math.Abs(monitor.Bottom - icon.CenterY) : int.MaxValue;
        int left = work.Left > monitor.Left ? Math.Abs(icon.CenterX - monitor.Left) : int.MaxValue;
        int right = work.Right < monitor.Right ? Math.Abs(monitor.Right - icon.CenterX) : int.MaxValue;
        int nearestInset = Math.Min(Math.Min(top, bottom), Math.Min(left, right));
        if (knownEdge is { } edge)
        {
            if (nearestInset == int.MaxValue)
                return edge;
            int knownDistance = edge switch
            {
                TaskbarEdge.Top => Math.Abs(icon.CenterY - monitor.Top),
                TaskbarEdge.Bottom => Math.Abs(monitor.Bottom - icon.CenterY),
                TaskbarEdge.Left => Math.Abs(icon.CenterX - monitor.Left),
                _ => Math.Abs(monitor.Right - icon.CenterX)
            };
            bool knownHasNoInset = edge switch
            {
                TaskbarEdge.Top => top == int.MaxValue,
                TaskbarEdge.Bottom => bottom == int.MaxValue,
                TaskbarEdge.Left => left == int.MaxValue,
                _ => right == int.MaxValue
            };
            if (knownHasNoInset && knownDistance <= nearestInset)
                return edge;
        }
        if (nearestInset == int.MaxValue)
        {
            top = Math.Abs(icon.CenterY - monitor.Top);
            bottom = Math.Abs(monitor.Bottom - icon.CenterY);
            left = Math.Abs(icon.CenterX - monitor.Left);
            right = Math.Abs(monitor.Right - icon.CenterX);
        }

        int nearest = Math.Min(Math.Min(top, bottom), Math.Min(left, right));
        return nearest == top ? TaskbarEdge.Top
            : nearest == bottom ? TaskbarEdge.Bottom
            : nearest == left ? TaskbarEdge.Left
            : TaskbarEdge.Right;
    }
}
