using ScreenKeeper.Services;

namespace ScreenKeeper.Tests;

public class HotkeyBindingTests {
    [Theory]
    [InlineData("PrintScreen", HotkeyModifiers.None, 0x2C, "PrintScreen")]
    [InlineData("Ctrl+PrintScreen", HotkeyModifiers.Ctrl, 0x2C, "Ctrl+PrintScreen")]
    [InlineData("Shift+PrintScreen", HotkeyModifiers.Shift, 0x2C, "Shift+PrintScreen")]
    [InlineData("Ctrl+Shift+PrintScreen", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x2C, "Ctrl+Shift+PrintScreen")]
    [InlineData("shift + strg + druck", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x2C, "Ctrl+Shift+PrintScreen")]
    [InlineData("Win+Alt+S", HotkeyModifiers.Win | HotkeyModifiers.Alt, 0x53, "Alt+Win+S")]
    [InlineData("Ctrl+F12", HotkeyModifiers.Ctrl, 0x7B, "Ctrl+F12")]
    [InlineData("Alt+5", HotkeyModifiers.Alt, 0x35, "Alt+5")]
    [InlineData("Ctrl+Insert", HotkeyModifiers.Ctrl, 0x2D, "Ctrl+Insert")]
    public void Parse_And_Format(string text, HotkeyModifiers modifiers, int virtualKey, string formatted) {
        HotkeyBinding binding = HotkeyBinding.Parse(text);
        Assert.Equal(modifiers, binding.Modifiers);
        Assert.Equal(virtualKey, binding.VirtualKey);
        Assert.Equal(formatted, binding.ToString());
        Assert.Equal(binding, HotkeyBinding.Parse(binding.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Hyper+PrintScreen")]
    [InlineData("Ctrl+Gibtsnicht")]
    public void TryParse_RejectsInvalid(string? text) {
        Assert.False(HotkeyBinding.TryParse(text, out HotkeyBinding? binding));
        Assert.Null(binding);
    }

    [Fact]
    public void Parse_ThrowsFormatException() {
        Assert.Throws<FormatException>(() => HotkeyBinding.Parse("Ctrl+"));
    }

    [Fact]
    public void IsPrintScreen() {
        Assert.True(HotkeyBinding.Parse("Ctrl+PrintScreen").IsPrintScreen);
        Assert.False(HotkeyBinding.Parse("Ctrl+P").IsPrintScreen);
    }
}
