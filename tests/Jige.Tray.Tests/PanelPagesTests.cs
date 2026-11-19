using Jige.Core;
using Jige.Tray;

namespace Jige.Tray.Tests;

public sealed class PanelPagesTests
{
    [Theory]
    [InlineData((int)PanelPage.Main, 0, 700, 157, 0)]
    [InlineData((int)PanelPage.Main, 1, 700, 247, 0)]
    [InlineData((int)PanelPage.Main, 30, 400, 391, 3)]
    [InlineData((int)PanelPage.Settings, 40, 700, 429, 0)]
    [InlineData((int)PanelPage.Devices, 0, 700, 153, 0)]
    [InlineData((int)PanelPage.Devices, 5, 700, 425, 5)]
    [InlineData((int)PanelPage.Devices, 40, 424, 370, 4)]
    [InlineData((int)PanelPage.Devices, 40, 425, 425, 5)]
    public void PageHeightIncludesChromeAndOnlyWholeRows(int page, int count, int available, int height, int rows)
    {
        Assert.Equal(new PanelSize(height, rows), PanelLayout.Size((PanelPage)page, count, available));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.2375, 0.8375)]
    [InlineData(1, 1)]
    public void ResizeUsesPrototypeCurveAndKeepsBottomAgainstTray(double progress, double expected)
    {
        double eased = PanelLayout.Ease(progress);
        Assert.Equal(expected, eased, 4);
        int height = (int)Math.Round(247 + (429 - 247) * eased);
        var tray = new Bounds(1830, 1040, 1850, 1080);
        var work = new Bounds(0, 0, 1920, 1040);
        var screen = new Bounds(0, 0, 1920, 1080);
        var popup = PopupPlacement.Place(tray, screen, work, 344, height, 12, false);
        Assert.Equal(1028, popup.Bottom);
        Assert.Equal(height, popup.Height);
    }

    [Theory]
    [InlineData(156, 174, 10, 0)]
    [InlineData(197.25, 186, 15, 0)]
    [InlineData(262, 197, 20, 0)]
    [InlineData(324, 186, 30, 0)]
    [InlineData(195.25, 186, 0, 0)]
    [InlineData(198, 198, 0, 0)]
    [InlineData(177, 303, 0, 30)]
    [InlineData(250, 314, 0, 60)]
    [InlineData(324, 326, 0, 120)]
    [InlineData(272, 314, 0, 0)]
    public void SegmentsHitTheDrawnButtonButIgnoreGapsAndTrailingEdges(
        float x, float y, int expectedThreshold, int expectedInterval)
    {
        Assert.Equal(expectedThreshold, Hit(PanelLayout.ThresholdOptions, x, y));
        Assert.Equal(expectedInterval, Hit(PanelLayout.PollOptions, x, y));

        static int Hit(ReadOnlySpan<PanelOption> options, float x, float y) =>
            options.ToArray().Where(option => option.Rect.Contains(x, y)).Select(option => option.Value).SingleOrDefault();
    }

    [Theory]
    [InlineData("inzone-h9:receiver", "Sony INZONE H9（一代接收器）", (int)DeviceKind.Headset, true)]
    [InlineData("inzone-h9:receiver", "Sony INZONE H9 II", (int)DeviceKind.Headset, false)]
    [InlineData("MOUSE:some-container", "G-Wolves HTX Ultra 8K", (int)DeviceKind.Mouse, true)]
    [InlineData("mouse:some-container", "G-Wolves HTX Mini 8K", (int)DeviceKind.Mouse, false)]
    [InlineData("mouse:some-container", "G-Wolves Fenrir Pro 8K", (int)DeviceKind.Mouse, false)]
    [InlineData("ulx:some-container", "G-Wolves HTX Ultra 8K", (int)DeviceKind.Mouse, false)]
    public void VerifiedBadgeUsesOriginalAdapterIdentity(string id, string name, int kind, bool verified)
    {
        Assert.Equal(verified, DeviceVerification.IsVerified(new(id, name, (DeviceKind)kind)));
    }
}
