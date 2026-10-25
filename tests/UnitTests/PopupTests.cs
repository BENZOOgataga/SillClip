using WindowSill.ClipboardHistory.Core;
using Xunit;

namespace UnitTests;

public sealed class PopupTests
{
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
