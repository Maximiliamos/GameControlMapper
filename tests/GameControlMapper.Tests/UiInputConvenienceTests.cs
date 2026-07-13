using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using GameControlMapper.UI;
using GameControlMapper.UI.Converters;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class UiInputConvenienceTests
{
    [Theory]
    [InlineData("0,5", 0.5)]
    [InlineData("0.5", 0.5)]
    [InlineData("1,25", 1.25)]
    public void FlexibleDouble_AcceptsCommaAndDot(string text, double expected)
    {
        var converter = new FlexibleDoubleConverter();
        var result = converter.ConvertBack(text, typeof(double), null!, CultureInfo.GetCultureInfo("ru-RU"));
        Assert.Equal(expected, Assert.IsType<double>(result));
    }

    [Fact]
    public void FlexibleDouble_InvalidText_DoesNotOverwriteSetting()
    {
        var converter = new FlexibleDoubleConverter();
        Assert.Same(System.Windows.Data.Binding.DoNothing,
            converter.ConvertBack("не число", typeof(double), null!, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(Key.Space, ModifierKeys.None, "Space")]
    [InlineData(Key.Q, ModifierKeys.Control, "Ctrl+Q")]
    [InlineData(Key.F8, ModifierKeys.None, "F8")]
    public void Recorder_FormatsPressedKeys(Key key, ModifierKeys modifiers, string expected)
    {
        Assert.Equal(expected, HotkeyCaptureFormatter.FormatKey(key, modifiers));
    }

    [Fact]
    public void Recorder_FormatsMouseButton()
    {
        Assert.Equal("MouseRight", HotkeyCaptureFormatter.FormatMouse(MouseButton.Right, ModifierKeys.None));
        Assert.Equal("MouseX1", HotkeyCaptureFormatter.FormatMouse(MouseButton.XButton1, ModifierKeys.None));
        Assert.Equal("MouseX2", HotkeyCaptureFormatter.FormatMouse(MouseButton.XButton2, ModifierKeys.None));
    }
}
