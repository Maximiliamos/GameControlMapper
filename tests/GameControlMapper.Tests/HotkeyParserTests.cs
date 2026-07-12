using System.Windows.Input;
using GameControlMapper.Services;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class HotkeyParserTests
{
    [Fact]
    public void Matches_ReturnsTrueForSingleKey()
    {
        var parser = new HotkeyParser();
        var key = KeyInterop.VirtualKeyFromKey(Key.F8);

        Assert.True(parser.Matches("F8", key, new HashSet<int> { key }));
    }

    [Fact]
    public void Matches_RequiresAllComboParts()
    {
        var parser = new HotkeyParser();
        var ctrl = KeyInterop.VirtualKeyFromKey(Key.LeftCtrl);
        var q = KeyInterop.VirtualKeyFromKey(Key.Q);

        Assert.True(parser.Matches("Ctrl+Q", q, new HashSet<int> { ctrl, q }));
        Assert.False(parser.Matches("Ctrl+Q", q, new HashSet<int> { q }));
    }
}
