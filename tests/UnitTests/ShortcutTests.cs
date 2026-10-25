using WindowSill.ClipboardHistory.Core;
using Xunit;

namespace UnitTests;

public sealed class ShortcutTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void OtherShortcutsPassThrough(bool win, bool modifier)
        => Assert.Equal(default, new WinVShortcutState().Process(0x56, true, false, win, modifier, false));

    [Theory]
    [InlineData(0x5Bu)]
    [InlineData(0x5Cu)]
    public void BareWindowsKeyPassesThrough(uint key)
    {
        var state = new WinVShortcutState();
        Assert.Equal(default, state.Process(key, true, false, false, false, false));
        Assert.Equal(default, state.Process(key, false, true, true, false, false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothReleaseOrdersOpenExactlyOnce(bool releaseVFirst)
    {
        var state = new WinVShortcutState();
        Assert.Equal((true, true, true), state.Process(0x56, true, false, true, false, false));
        Assert.Equal((true, false, false), state.Process(0x56, true, false, true, false, false));
        if (releaseVFirst) Assert.Equal((true, false, false), state.Process(0x56, false, true, true, false, false));
        Assert.Equal(default, state.Process(0x5B, false, true, true, false, false));
        if (!releaseVFirst) Assert.Equal((true, false, false), state.Process(0x56, false, true, false, false, false));
        Assert.Equal(default, state.Process(0x5B, false, true, false, false, false));
        Assert.Equal(default, state.Process(0x56, true, false, false, false, false));
    }

    [Fact]
    public void WindowsKeyReleasesDoNotOpenThePopupAgain()
    {
        var state = new WinVShortcutState();
        Assert.Equal((true, true, true), state.Process(0x56, true, false, true, false, true));
        Assert.Equal(default, state.Process(0x5B, false, true, true, false, true));
        Assert.Equal(default, state.Process(0x5C, false, true, true, false, false));
    }

    [Fact]
    public void ResetClearsPendingAndSuppression()
    {
        var state = new WinVShortcutState();
        state.Process(0x56, true, false, true, false, false);
        state.Reset();
        Assert.Equal(default, state.Process(0x5B, false, true, true, false, false));
        Assert.Equal(default, state.Process(0x56, false, true, false, false, false));
    }

    [Fact]
    public void GameplayKeysAlwaysPassThroughEvenDuringWinV()
    {
        var state = new WinVShortcutState();
        state.Process(0x56, true, false, true, false, false);
        foreach (uint key in new uint[] { 0x57, 0x41, 0x53, 0x44, 0x5A, 0x51, 0x20, 0x10, 0x11, 0x25, 0x26, 0x27, 0x28 })
        {
            Assert.Equal(default, state.Process(key, true, false, true, false, false));
            Assert.Equal(default, state.Process(key, true, false, true, false, false));
            Assert.Equal(default, state.Process(key, false, true, true, false, false));
        }
    }
}
