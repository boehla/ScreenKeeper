using ScreenKeeper.Services;

namespace ScreenKeeper.Tests;

public class Version090TrayTests {
    [Fact]
    public void Tooltip_ListsConfiguredHotkeysOnly() {
        AppSettings settings = new AppSettings {
            HotkeyCaptureEdit = "PrintScreen",
            HotkeyCaptureSave = "",
            HotkeyCaptureDelayed = "Alt+PrintScreen",
            HotkeyHistory = "Ctrl+Shift+H"
        };
        string text = TrayText.BuildTooltip(settings, false);
        Assert.Equal("ScreenKeeper\nDruck: Aufnahme\nAlt+Druck: verzögerte Aufnahme\nStrg+Umschalt+H: History", text);
    }

    [Fact]
    public void Tooltip_ShowsUpdateAndStaysWithinLimit() {
        AppSettings settings = new AppSettings {
            HotkeyCaptureEdit = "Ctrl+Alt+Shift+Win+PrintScreen",
            HotkeyCaptureSave = "Ctrl+Alt+Shift+Win+F12",
            HotkeyCaptureDelayed = "Ctrl+Alt+Shift+Win+F11",
            HotkeyHistory = "Ctrl+Alt+Shift+Win+F10"
        };
        string text = TrayText.BuildTooltip(settings, true);
        Assert.StartsWith("ScreenKeeper · Update verfügbar\n", text);
        Assert.True(text.Length <= TrayText.MaxLength);
    }

    [Fact]
    public void Gesture_IsEmptyWithoutHotkey() {
        Assert.Equal("", TrayText.GetGesture(""));
        Assert.Equal("Strg+Druck", TrayText.GetGesture("Ctrl+PrintScreen"));
    }
}
