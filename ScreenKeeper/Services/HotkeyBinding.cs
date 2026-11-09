using System.Globalization;
using System.Text;
using System.Windows.Input;
using ScreenKeeper.Resources;

namespace ScreenKeeper.Services;

/// <summary>
/// Modifizierer, Werte entsprechen MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN von RegisterHotKey.
/// </summary>
[Flags]
public enum HotkeyModifiers {
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8
}

/// <summary>
/// Ein Hotkey (Modifizierer + virtueller Tastencode), als String z. B. <c>"Ctrl+Shift+PrintScreen"</c>.
/// </summary>
public sealed record HotkeyBinding(HotkeyModifiers Modifiers, int VirtualKey) {
    public const int VkPrintScreen = 0x2C;

    public bool IsPrintScreen => VirtualKey == VkPrintScreen;

    public static HotkeyBinding Parse(string text) {
        if(!TryParse(text, out HotkeyBinding? binding) || binding == null) throw new FormatException($"Ungültiger Hotkey: \"{text}\"");
        return binding;
    }

    public static bool TryParse(string? text, out HotkeyBinding? binding) {
        binding = null;
        if(string.IsNullOrWhiteSpace(text)) return false;
        string[] parts = text.Split('+', StringSplitOptions.TrimEntries);
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        for(int i = 0; i < parts.Length - 1; i++) {
            HotkeyModifiers? modifier = parseModifier(parts[i]);
            if(modifier == null) return false;
            modifiers |= modifier.Value;
        }
        int? key = parseKey(parts[parts.Length - 1]);
        if(key == null) return false;
        binding = new HotkeyBinding(modifiers, key.Value);
        return true;
    }

    public override string ToString() {
        StringBuilder text = new StringBuilder();
        if(Modifiers.HasFlag(HotkeyModifiers.Ctrl)) text.Append("Ctrl+");
        if(Modifiers.HasFlag(HotkeyModifiers.Alt)) text.Append("Alt+");
        if(Modifiers.HasFlag(HotkeyModifiers.Shift)) text.Append("Shift+");
        if(Modifiers.HasFlag(HotkeyModifiers.Win)) text.Append("Win+");
        text.Append(formatKey(VirtualKey));
        return text.ToString();
    }

    /// <summary>
    /// Buchstaben, Ziffern und ähnliche Tasten ohne Strg/Alt/Win würden normales Tippen blockieren.
    /// Ohne Modifizierer erlaubt sind nur Druck, F1–F24, Pause und Rollen.
    /// </summary>
    public bool NeedsModifier {
        get {
            HotkeyModifiers strong = Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win);
            if(strong != HotkeyModifiers.None) return false;
            bool functionKey = VirtualKey >= 0x70 && VirtualKey <= 0x87;
            return !(IsPrintScreen || functionKey || VirtualKey == 0x13 || VirtualKey == 0x91);
        }
    }

    /// <summary>
    /// Anzeige für die Oberfläche, z. B. <c>Strg+Umschalt+Druck</c>.
    /// </summary>
    public string ToDisplayString() {
        StringBuilder text = new StringBuilder();
        if(Modifiers.HasFlag(HotkeyModifiers.Ctrl)) text.Append(Strings.KeyCtrl).Append('+');
        if(Modifiers.HasFlag(HotkeyModifiers.Alt)) text.Append(Strings.KeyAlt).Append('+');
        if(Modifiers.HasFlag(HotkeyModifiers.Shift)) text.Append(Strings.KeyShift).Append('+');
        if(Modifiers.HasFlag(HotkeyModifiers.Win)) text.Append(Strings.KeyWin).Append('+');
        text.Append(IsPrintScreen ? Strings.KeyPrintScreen : formatKey(VirtualKey));
        return text.ToString();
    }

    /// <summary>
    /// Anzeigetext für einen gespeicherten Hotkey-String (leer = aus, ungültig = Text unverändert).
    /// </summary>
    public static string ToDisplayString(string? text) {
        if(string.IsNullOrWhiteSpace(text)) return "";
        return TryParse(text, out HotkeyBinding? binding) && binding != null ? binding.ToDisplayString() : text;
    }

    /// <summary>
    /// Hotkey aus einem Tastendruck (Tastenaufzeichnung). Null bei reinen Modifizierer-Tasten.
    /// </summary>
    public static HotkeyBinding? FromKey(Key key, ModifierKeys modifierKeys) {
        if(key == Key.None || isModifierKey(key) || key == Key.ImeProcessed || key == Key.DeadCharProcessed) return null;
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        if(modifierKeys.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Ctrl;
        if(modifierKeys.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if(modifierKeys.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if(modifierKeys.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;
        int virtualKey = key == Key.Snapshot ? VkPrintScreen : KeyInterop.VirtualKeyFromKey(key);
        if(virtualKey <= 0 || virtualKey >= 0xFF) return null;
        return new HotkeyBinding(modifiers, virtualKey);
    }

    private static HotkeyModifiers? parseModifier(string text) {
        switch(text.ToLowerInvariant()) {
            case "ctrl":
            case "control":
            case "strg":
                return HotkeyModifiers.Ctrl;
            case "alt":
                return HotkeyModifiers.Alt;
            case "shift":
            case "umschalt":
                return HotkeyModifiers.Shift;
            case "win":
            case "windows":
                return HotkeyModifiers.Win;
        }
        return null;
    }

    private static int? parseKey(string text) {
        if(text.Length == 0) return null;
        switch(text.ToLowerInvariant()) {
            case "printscreen":
            case "print":
            case "prtsc":
            case "snapshot":
            case "druck":
                return VkPrintScreen;
        }
        if(text.Length == 1) {
            char c = char.ToUpperInvariant(text[0]);
            if((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
        }
        if((text[0] == 'F' || text[0] == 'f') && int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int fNumber) && fNumber >= 1 && fNumber <= 24) {
            return 0x70 + fNumber - 1;
        }
        // Fallback-Format von formatKey für Tasten ohne Namen, z. B. "0xE2"
        if(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hexKey) && hexKey > 0 && hexKey < 0xFF) {
            return hexKey;
        }
        if(!char.IsDigit(text[0]) && Enum.TryParse(text, true, out Key key) && key != Key.None && !isModifierKey(key)) {
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if(vk != 0) return vk;
        }
        return null;
    }

    private static string formatKey(int virtualKey) {
        if(virtualKey == VkPrintScreen) return "PrintScreen";
        if((virtualKey >= 'A' && virtualKey <= 'Z') || (virtualKey >= '0' && virtualKey <= '9')) return ((char)virtualKey).ToString();
        if(virtualKey >= 0x70 && virtualKey <= 0x87) return "F" + (virtualKey - 0x70 + 1).ToString(CultureInfo.InvariantCulture);
        Key key = KeyInterop.KeyFromVirtualKey(virtualKey);
        return key == Key.None ? "0x" + virtualKey.ToString("X2", CultureInfo.InvariantCulture) : key.ToString();
    }

    private static bool isModifierKey(Key key) {
        return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;
    }
}
