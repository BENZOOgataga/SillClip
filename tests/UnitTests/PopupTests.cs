using WindowSill.ClipboardHistory.Core;
using Xunit;

namespace UnitTests;

public sealed class PopupTests
{
    [Theory]
    [InlineData(-1800, 200, 1, 20, true)]
    [InlineData(200, 200, 1, 20, false)]
    [InlineData(-32000, -32000, 1, 20, false)]
    [InlineData(0, 200, 1, 20, false)]
    [InlineData(-1920, 0, 1, 20, true)]
    [InlineData(-1800, 1080, 1, 20, false)]
    [InlineData(-1800, 200, 0, 20, false)]
    [InlineData(-1800, 200, 1, 0, false)]
    public void CaretCannotChooseADifferentMonitor(int x, int y, int width, int height, bool accepted)
    {
        var caret = new ScreenRect(x, y, width, height);
        var result = PopupPlacement.CaretOnPointerMonitor(caret, new(-1920, 0, 1920, 1080));
        Assert.Equal(accepted ? caret : (ScreenRect?)null, result);
    }

    [Fact]
    public void IntermittentCaretAvailabilityKeepsThePointerMonitor()
    {
        var monitor = new ScreenRect(-1920, -200, 1920, 1080);
        var pointer = new ScreenPoint(-1700, 100);
        ScreenRect?[] reports = [new(-1800, 200, 1, 20), null, new(500, 300, 1, 20), new(-1800, 200, 1, 20)];
        foreach (var report in reports)
        {
            var caret = PopupPlacement.CaretOnPointerMonitor(report, monitor);
            var point = PopupPlacement.Automatic(caret?.X ?? pointer.X, caret?.Y ?? pointer.Y,
                caret?.Height ?? 0, 360, 400, monitor.X, monitor.Y, monitor.Width, monitor.Height, 8);
            Assert.InRange(point.X, monitor.X, monitor.X + monitor.Width - 360);
            Assert.InRange(point.Y, monitor.Y, monitor.Y + monitor.Height - 400);
            if (caret is null) Assert.Equal((-1700, 108), point);
        }
    }

    [Fact]
    public void CaretPlacementUsesSpaceBelowTheInsertionPoint()
        => Assert.Equal((200, 328), PopupPlacement.Automatic(200, 300, 20, 360, 400, 0, 0, 1920, 1080, 8));

    [Fact]
    public void CaretNearBottomPlacesPopupAboveIt()
        => Assert.Equal((200, 592), PopupPlacement.Automatic(200, 1000, 20, 360, 400, 0, 0, 1920, 1080, 8));

    [Fact]
    public void MissingCaretFallsBackToWorkAreaCorner()
        => Assert.Equal((1552, 672), PopupPlacement.Automatic(null, null, 0, 360, 400, 0, 0, 1920, 1080, 8));

    [Fact]
    public void CaretNearRightEdgeStaysOnItsMonitor()
        => Assert.Equal((1552, 328), PopupPlacement.Automatic(1900, 300, 20, 360, 400, 0, 0, 1920, 1080, 8));

    [Fact]
    public void NegativeMonitorCoordinatesArePreserved()
        => Assert.Equal((-368, 472), PopupPlacement.Automatic(null, null, 0, 360, 400, -1920, -200, 1920, 1080, 8));

    [Theory]
    [InlineData(-1, 0, 1, -1)]
    [InlineData(-1, 5, 1, 0)]
    [InlineData(0, 5, -1, 0)]
    [InlineData(0, 5, 1, 1)]
    [InlineData(4, 5, 1, 4)]
    [InlineData(4, 5, -1, 3)]
    public void KeyboardNavigationHandlesEmptyResultsAndBoundaries(int current, int count, int direction, int expected)
        => Assert.Equal(expected, PopupNavigation.Next(current, count, direction));
}
