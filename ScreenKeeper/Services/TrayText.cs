using System.Text;
using ScreenKeeper.Resources;

namespace ScreenKeeper.Services;

/// <summary>
/// Texte des Tray-Symbols aus den eingestellten Hotkeys (UI-frei, testbar): Tooltip mit einer Zeile je Hotkey und die
/// Tastenangaben für das Kontextmenü.
/// </summary>
public static class TrayText {
    /// <summary>Längster Tooltip, den das Tray-Symbol annimmt.</summary>
    public const int MaxLength = 127;

    /// <summary>
    /// Tooltip: Programmname (mit Update-Hinweis) und je eingestelltem Hotkey eine Zeile „Tasten: Aktion“. Nicht
    /// eingestellte Hotkeys entfallen; passt eine Zeile nicht mehr in <see cref="MaxLength"/>, entfällt sie ebenfalls.
    /// </summary>
    public static string BuildTooltip(AppSettings settings, bool updateAvailable) {
        StringBuilder text = new StringBuilder(Strings.TrayTitle);
        if(updateAvailable) text.Append(Strings.TrayUpdateSuffix);
        append(text, settings.HotkeyCaptureEdit, Strings.TrayHotkeyCapture);
        append(text, settings.HotkeyCaptureSave, Strings.TrayHotkeySave);
        append(text, settings.HotkeyCaptureDelayed, Strings.TrayHotkeyDelayed);
        append(text, settings.HotkeyHistory, Strings.TrayHotkeyHistory);
        return text.ToString();
    }

    /// <summary>Tastenangabe für einen Menüeintrag, z. B. <c>Strg+Druck</c>; leer, wenn kein Hotkey eingestellt ist.</summary>
    public static string GetGesture(string? hotkey) {
        return HotkeyBinding.ToDisplayString(hotkey);
    }

    private static void append(StringBuilder text, string? hotkey, string label) {
        string display = GetGesture(hotkey);
        if(display.Length == 0) return;
        string line = $"\n{display}: {label}";
        if(text.Length + line.Length > MaxLength) return;
        text.Append(line);
    }
}
