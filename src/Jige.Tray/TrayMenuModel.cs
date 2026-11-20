namespace Jige.Tray;

internal enum TrayMenuCommand
{
    None,
    Refresh,
    Settings,
    Updates,
    Diagnostics,
    Exit
}

internal sealed record TrayMenuItem(TrayMenuCommand Command, string Label, bool SeparatorBefore = false);

internal static class TrayMenuModel
{
    public const int Width = 176;
    public const float PaddingX = 5;
    public const float PaddingY = 5;
    public const float ItemHeight = 32;
    public const float ItemGap = 2;
    public const float SeparatorHeight = 9;
    public const float ItemRadius = 4;
    public const float CornerRadius = 8;

    public static readonly TrayMenuItem[] Items =
    [
        new(TrayMenuCommand.Refresh, "立即刷新"),
        new(TrayMenuCommand.Settings, "设置"),
        new(TrayMenuCommand.Updates, "检查更新"),
        new(TrayMenuCommand.Diagnostics, "诊断信息"),
        new(TrayMenuCommand.Exit, "退出", SeparatorBefore: true)
    ];

    public static int TotalHeight => ComputeTotalHeight();

    public static PanelRect ItemRect(int index)
    {
        if ((uint)index >= (uint)Items.Length)
            return default;
        float y = ItemTop(index);
        return new(PaddingX, y, Width - PaddingX * 2, ItemHeight);
    }

    public static float? SeparatorY(int index)
    {
        if ((uint)index >= (uint)Items.Length || !Items[index].SeparatorBefore)
            return null;
        return ItemTop(index) - SeparatorHeight / 2f;
    }

    public static int Hit(float x, float y)
    {
        for (int i = 0; i < Items.Length; i++)
        {
            if (ItemRect(i).Contains(x, y))
                return i;
        }
        return -1;
    }

    public static TrayMenuCommand CommandAt(int index) =>
        (uint)index < (uint)Items.Length ? Items[index].Command : TrayMenuCommand.None;

    public static int MoveSelection(int current, int delta)
    {
        if (Items.Length == 0)
            return -1;
        if (current < 0 || current >= Items.Length)
            return delta >= 0 ? 0 : Items.Length - 1;
        int step = delta % Items.Length;
        return (current + step + Items.Length) % Items.Length;
    }

    public static Bounds Place(Bounds anchor, Bounds monitor, Bounds work, int widthPx, int heightPx,
        int gapPx = 10, int edgeMarginPx = 8) =>
        HoverCardPlacement.Place(anchor, monitor, work, widthPx, heightPx, gapPx, edgeMarginPx);

    private static float ItemTop(int targetIndex)
    {
        float y = PaddingY;
        for (int i = 0; i < targetIndex; i++)
        {
            y += ItemHeight;
            if (i + 1 < Items.Length)
                y += Items[i + 1].SeparatorBefore ? SeparatorHeight : ItemGap;
        }
        return y;
    }

    private static int ComputeTotalHeight()
    {
        if (Items.Length == 0)
            return (int)(PaddingY * 2);
        return (int)MathF.Ceiling(ItemTop(Items.Length - 1) + ItemHeight + PaddingY);
    }
}
