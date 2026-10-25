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
        Assert.Equal((true, true, false), state.Process(0x56, true, false, true, false, false));
        Assert.Equal((true, false, false), state.Process(0x56, true, false, true, false, false));
        if (releaseVFirst) Assert.Equal((true, false, false), state.Process(0x56, false, true, true, false, false));
        Assert.Equal((false, false, true), state.Process(0x5B, false, true, true, false, false));
        if (!releaseVFirst) Assert.Equal((true, false, false), state.Process(0x56, false, true, false, false, false));
        Assert.Equal(default, state.Process(0x5B, false, true, false, false, false));
        Assert.Equal(default, state.Process(0x56, true, false, false, false, false));
    }

    [Fact]
    public void ReleasingOnlyOneOfTwoWindowsKeysWaits()
    {
        var state = new WinVShortcutState();
        state.Process(0x56, true, false, true, false, true);
        Assert.Equal(default, state.Process(0x5B, false, true, true, false, true));
        Assert.Equal((false, false, true), state.Process(0x5C, false, true, true, false, false));
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
}
