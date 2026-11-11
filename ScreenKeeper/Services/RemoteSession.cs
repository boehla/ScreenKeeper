namespace ScreenKeeper.Services;

/// <summary>
/// Erkennung von Remotedesktop-Fenstern über die Fensterklasse (UI-frei, testbar).
/// </summary>
public static class RemoteSession {
    /// <summary>Fensterklasse des Remotedesktop-Clients (mstsc) – auch im Vollbild.</summary>
    public const string DefaultWindowClasses = "TscShellContainerClass";

    /// <summary>
    /// Klassenliste aus den Einstellungen: durch <c>;</c> oder <c>,</c> getrennt, Leerraum wird entfernt, doppelte und
    /// leere Einträge fallen weg (Groß-/Kleinschreibung egal).
    /// </summary>
    public static IReadOnlyList<string> ParseWindowClasses(string? text) {
        if(string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsRemoteWindowClass(string? className, IReadOnlyList<string> classes) {
        if(string.IsNullOrEmpty(className)) return false;
        foreach(string candidate in classes) {
            if(string.Equals(candidate, className, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

/// <summary>Ein zu sendender Tastendruck (virtueller Code, Scancode, erweiterte Taste, Loslassen).</summary>
public readonly record struct KeyStroke(int VirtualKey, ushort ScanCode, bool Extended, bool Up);

/// <summary>
/// Eingabefolge für den Durchreich-Hotkey: Die Remotesitzung soll ein reines Druck sehen, obwohl der Benutzer noch
/// Zusatztasten hält. Also erst die gehaltenen Zusatztasten loslassen (Strg/Umschalt zuerst, dann Alt/Win), Druck
/// drücken und loslassen und danach die Zusatztasten in umgekehrter Reihenfolge wieder drücken.
/// Wird Alt oder Win ohne Strg gehalten, kommt vor deren Loslassen ein kurzes Strg (Maskentaste), damit die Sitzung
/// das nicht als einzelnes Antippen wertet (Alt allein öffnet das Menü, Win allein das Startmenü).
/// </summary>
public static class RemoteKeySequence {
    public const int VkSnapshot = 0x2C;
    private const ushort scanSnapshot = 0x37;

    public static IReadOnlyList<KeyStroke> Build(IEnumerable<int> heldModifiers) {
        HashSet<int> held = new HashSet<int>(heldModifiers);
        List<int> release = new List<int>();
        foreach(int key in new[] { ModifierTracker.VkLControl, ModifierTracker.VkRControl, ModifierTracker.VkLShift, ModifierTracker.VkRShift, ModifierTracker.VkLMenu, ModifierTracker.VkRMenu, ModifierTracker.VkLWin, ModifierTracker.VkRWin }) {
            if(held.Contains(key)) release.Add(key);
        }
        bool ctrlHeld = held.Contains(ModifierTracker.VkLControl) || held.Contains(ModifierTracker.VkRControl);
        bool altOrWinHeld = release.Any(k => k is ModifierTracker.VkLMenu or ModifierTracker.VkRMenu or ModifierTracker.VkLWin or ModifierTracker.VkRWin);

        List<KeyStroke> strokes = new List<KeyStroke>();
        bool maskSent = false;
        foreach(int key in release) {
            bool altOrWin = key is ModifierTracker.VkLMenu or ModifierTracker.VkRMenu or ModifierTracker.VkLWin or ModifierTracker.VkRWin;
            if(altOrWin && altOrWinHeld && !ctrlHeld && !maskSent) {
                strokes.Add(stroke(ModifierTracker.VkLControl, false));
                strokes.Add(stroke(ModifierTracker.VkLControl, true));
                maskSent = true;
            }
            strokes.Add(stroke(key, true));
        }
        strokes.Add(new KeyStroke(VkSnapshot, scanSnapshot, true, false));
        strokes.Add(new KeyStroke(VkSnapshot, scanSnapshot, true, true));
        for(int i = release.Count - 1; i >= 0; i--) strokes.Add(stroke(release[i], false));
        return strokes;
    }

    private static KeyStroke stroke(int virtualKey, bool up) {
        (ushort scan, bool extended) = virtualKey switch {
            ModifierTracker.VkLControl => ((ushort)0x1D, false),
            ModifierTracker.VkRControl => ((ushort)0x1D, true),
            ModifierTracker.VkLShift => ((ushort)0x2A, false),
            ModifierTracker.VkRShift => ((ushort)0x36, false),
            ModifierTracker.VkLMenu => ((ushort)0x38, false),
            ModifierTracker.VkRMenu => ((ushort)0x38, true),
            ModifierTracker.VkLWin => ((ushort)0x5B, true),
            ModifierTracker.VkRWin => ((ushort)0x5C, true),
            _ => ((ushort)0, false)
        };
        return new KeyStroke(virtualKey, scan, extended, up);
    }
}
