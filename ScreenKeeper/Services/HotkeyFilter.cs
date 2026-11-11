namespace ScreenKeeper.Services;

/// <summary>
/// Wann eine Tastenkombination über den Tastatur-Hook abgefangen wird.
/// </summary>
public enum HotkeyScope {
    /// <summary>Immer (z. B. Druck-Kombination, die sich nicht per RegisterHotKey registrieren ließ).</summary>
    Always,
    /// <summary>
    /// Nur solange ein Remotedesktop-Fenster im Vordergrund ist. Sonst kommt die Kombination per RegisterHotKey
    /// (WM_HOTKEY) an bzw. ist aus – der Hook lässt sie dann unverändert durch, damit nichts doppelt auslöst.
    /// </summary>
    RemoteOnly
}

/// <summary>Eine vom Tastatur-Hook abzufangende Kombination.</summary>
public sealed record HookBinding(HotkeyAction Action, HotkeyBinding Binding, HotkeyScope Scope);

/// <summary>
/// Entscheidet für den Tastatur-Hook, welche Tastendrücke abgefangen werden (UI-frei, testbar). Ein abgefangenes
/// Drücken löst die Aktion genau einmal aus (automatische Wiederholungen nicht); das zugehörige Loslassen wird ebenfalls
/// unterdrückt – auch wenn die Zusatztasten inzwischen losgelassen wurden. Alles andere geht unverändert durch.
/// <see cref="Process"/> läuft nur im Hook-Thread; <see cref="SetBindings"/> und <see cref="RemoteActive"/> dürfen aus
/// anderen Threads gesetzt werden.
/// </summary>
public sealed class HotkeyFilter {
    private volatile HookBinding[] bindings;
    private volatile bool remoteActive;
    private int swallowedKey;

    public HotkeyFilter(IEnumerable<HookBinding> bindings) {
        this.bindings = bindings.ToArray();
    }

    /// <summary>Gerade ist ein Remotedesktop-Fenster im Vordergrund (schaltet die <see cref="HotkeyScope.RemoteOnly"/>-Kombinationen scharf).</summary>
    public bool RemoteActive {
        get => remoteActive;
        set => remoteActive = value;
    }

    public IReadOnlyList<HookBinding> Bindings => bindings;

    /// <summary>Es gibt Kombinationen, die nur im Remote-Fall gelten (dann lohnt es sich, den Vordergrund zu beobachten).</summary>
    public bool HasRemoteBindings => bindings.Any(b => b.Scope == HotkeyScope.RemoteOnly);

    /// <summary>
    /// Der Low-Level-Hook muss gerade installiert sein: Es gibt eine immer geltende Kombination oder ein Remote-Fenster
    /// ist im Vordergrund. Sonst bleibt er entfernt und das Verhalten ist exakt wie ohne Hook.
    /// </summary>
    public bool NeedsHook {
        get {
            HookBinding[] current = bindings;
            if(current.Any(b => b.Scope == HotkeyScope.Always)) return true;
            return remoteActive && current.Any(b => b.Scope == HotkeyScope.RemoteOnly);
        }
    }

    /// <summary>Kombinationen austauschen (z. B. Esc während des Countdowns); ein gerade unterdrücktes Loslassen bleibt erhalten.</summary>
    public void SetBindings(IEnumerable<HookBinding> newBindings) {
        bindings = newBindings.ToArray();
    }

    /// <summary>
    /// True = Taste unterdrücken. <paramref name="action"/> = auszulösende Aktion (null = keine).
    /// </summary>
    public bool Process(int virtualKey, bool down, HotkeyModifiers modifiers, out HotkeyAction? action) {
        action = null;
        if(!down) {
            if(swallowedKey == 0 || virtualKey != swallowedKey) return false;
            swallowedKey = 0;
            return true;
        }
        // automatische Wiederholung der gerade abgefangenen Taste
        if(virtualKey == swallowedKey) return true;
        bool remote = remoteActive;
        foreach(HookBinding binding in bindings) {
            if(binding.Binding.VirtualKey != virtualKey || binding.Binding.Modifiers != modifiers) continue;
            if(binding.Scope == HotkeyScope.RemoteOnly && !remote) continue;
            swallowedKey = virtualKey;
            action = binding.Action;
            return true;
        }
        return false;
    }
}

/// <summary>
/// Merkt sich, welche Zusatztasten (links/rechts getrennt) laut Hook gedrückt sind. Nötig im Remotedesktop-Vollbild:
/// mstsc schluckt dort die Tasten in seinem eigenen Hook, dann stimmt <c>GetAsyncKeyState</c> lokal nicht.
/// Nur im Hook-Thread benutzen.
/// </summary>
public sealed class ModifierTracker {
    public const int VkLShift = 0xA0;
    public const int VkRShift = 0xA1;
    public const int VkLControl = 0xA2;
    public const int VkRControl = 0xA3;
    public const int VkLMenu = 0xA4;
    public const int VkRMenu = 0xA5;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;

    /// <summary>Alle unterschiedenen Zusatztasten in fester Reihenfolge.</summary>
    public static readonly IReadOnlyList<int> ModifierKeys = new[] { VkLControl, VkRControl, VkLShift, VkRShift, VkLMenu, VkRMenu, VkLWin, VkRWin };

    private readonly HashSet<int> held = new HashSet<int>();

    /// <summary>Gerade gedrückte Zusatztasten (in der Reihenfolge von <see cref="ModifierKeys"/>).</summary>
    public IReadOnlyList<int> Held => ModifierKeys.Where(held.Contains).ToList();

    public static bool IsModifier(int virtualKey) {
        return virtualKey is VkLShift or VkRShift or VkLControl or VkRControl or VkLMenu or VkRMenu or VkLWin or VkRWin
            or 0x10 or 0x11 or 0x12;
    }

    /// <summary>Tastenereignis verbuchen (nur Zusatztasten zählen; die allgemeinen Codes 0x10–0x12 als linke Taste).</summary>
    public void Update(int virtualKey, bool down) {
        int key = virtualKey switch {
            0x10 => VkLShift,
            0x11 => VkLControl,
            0x12 => VkLMenu,
            _ => virtualKey
        };
        if(!ModifierKeys.Contains(key)) return;
        if(down) {
            held.Add(key);
        } else {
            held.Remove(key);
        }
    }

    public void Clear() {
        held.Clear();
    }

    /// <summary>Gehaltene Zusatztasten als <see cref="HotkeyModifiers"/>.</summary>
    public HotkeyModifiers Modifiers => ToModifiers(held);

    public static HotkeyModifiers ToModifiers(IEnumerable<int> keys) {
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        foreach(int key in keys) {
            switch(key) {
                case VkLControl:
                case VkRControl:
                    modifiers |= HotkeyModifiers.Ctrl;
                    break;
                case VkLShift:
                case VkRShift:
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case VkLMenu:
                case VkRMenu:
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case VkLWin:
                case VkRWin:
                    modifiers |= HotkeyModifiers.Win;
                    break;
            }
        }
        return modifiers;
    }
}
