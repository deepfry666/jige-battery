using System.Numerics;
using Jige.Core;

namespace Jige.Tray;

internal static class TrayDeviceShape
{
    public static (int Width, int Height) Dimensions(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => (10, 16),
        DeviceKind.Keyboard => (15, 9),
        DeviceKind.Headset => (16, 13),
        _ => (14, 12)
    };

    // 路径坐标相对于剪影边界，内部路径用奇偶填充挖空。关键缺口至少宽 2 个设计像素，
    // 即使在系统支持的最小 8px 图标上缩小一半，也仍有至少 1 个物理像素。
    private static readonly Vector2[][] Mouse =
    [
        [new(2, 0), new(8, 0), new(9.5f, 1.5f), new(10, 3), new(10, 6), new(0, 6), new(0, 3), new(.5f, 1.5f)],
        Cutout(4, 2, 2, 3),
        [new(0, 8), new(10, 8), new(10, 13), new(9.25f, 14.75f), new(7.75f, 16),
            new(2.25f, 16), new(.75f, 14.75f), new(0, 13)]
    ];

    private static readonly Vector2[][] Keyboard =
    [
        [new(1, 0), new(14, 0), new(15, 1), new(15, 8), new(14, 9), new(1, 9), new(0, 8), new(0, 1)],
        Cutout(1.25f, 1, 2, 2), Cutout(3.875f, 1, 2, 2), Cutout(6.5f, 1, 2, 2),
        Cutout(9.125f, 1, 2, 2), Cutout(11.75f, 1, 2, 2),
        Cutout(2.5625f, 3.5f, 2, 2), Cutout(5.1875f, 3.5f, 2, 2),
        Cutout(7.8125f, 3.5f, 2, 2), Cutout(10.4375f, 3.5f, 2, 2),
        Cutout(4, 6.25f, 7, 2)
    ];

    private static readonly Vector2[][] Headset =
    [
        [new(0, 7), new(1, 6), new(1, 4), new(2, 2), new(4, .5f), new(5.5f, 0),
            new(10.5f, 0), new(12, .5f), new(14, 2), new(15, 4), new(15, 6), new(16, 7),
            new(16, 11.5f), new(14.75f, 13), new(12, 13), new(11, 11.5f), new(11, 7),
            new(12.75f, 6), new(12.75f, 4.5f), new(11.5f, 3), new(9.75f, 2), new(6.25f, 2),
            new(4.5f, 3), new(3.25f, 4.5f), new(3.25f, 6), new(5, 7), new(5, 11.5f),
            new(4, 13), new(1.25f, 13), new(0, 11.5f)]
    ];

    private static readonly Vector2[][] Other =
    [
        [new(2, 0), new(12, 0), new(14, 2), new(14, 10), new(12, 12), new(2, 12), new(0, 10), new(0, 2)],
        Cutout(3, 3, 2, 2),
        Cutout(5, 7, 6, 2)
    ];

    public static ReadOnlySpan<Vector2[]> Contours(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => Mouse,
        DeviceKind.Keyboard => Keyboard,
        DeviceKind.Headset => Headset,
        _ => Other
    };

    public static Matrix3x2 Transform(DeviceKind kind, IconRect glyph)
    {
        var (width, height) = Dimensions(kind);
        return Matrix3x2.CreateScale((float)glyph.Width / width, (float)glyph.Height / height)
            * Matrix3x2.CreateTranslation(glyph.X, glyph.Y);
    }

    private static Vector2[] Cutout(float x, float y, float width, float height) =>
        [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)];
}
