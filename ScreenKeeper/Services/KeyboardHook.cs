using System.Runtime.InteropServices;
using ScreenKeeper.Services.Native;

namespace ScreenKeeper.Services;

/// <summary>
/// Entscheidet für den Tastatur-Hook, welche Druck-Tastendrücke abgefangen werden (UI-frei, testbar).
/// Ein abgefangenes Drücken löst die Aktion genau einmal aus (Wiederholungen nicht); das zugehörige Loslassen wird
/// ebenfalls unterdrückt – auch wenn die Zusatztasten inzwischen losgelassen wurden.
/// </summary>
public sealed class PrintScreenFilter {
    private readonly KeyValuePair<HotkeyAction, HotkeyModifiers>[] bindings;
    private bool keyDown;

    public PrintScreenFilter(IEnumerable<KeyValuePair<HotkeyAction, HotkeyBinding>> bindings) {
        this.bindings = bindings
            .Where(pair => pair.Value.IsPrintScreen)
            .Select(pair => new KeyValuePair<HotkeyAction, HotkeyModifiers>(pair.Key, pair.Value.Modifiers))
            .ToArray();
    }

    /// <summary>
    /// True = Taste unterdrücken. <paramref name="action"/> = auszulösende Aktion (null = keine).
    /// </summary>
    public bool Process(int virtualKey, bool down, HotkeyModifiers modifiers, out HotkeyAction? action) {
        action = null;
        if(virtualKey != HotkeyBinding.VkPrintScreen) return false;
        if(!down) {
            bool suppress = keyDown;
            keyDown = false;
            return suppress;
        }
        foreach(KeyValuePair<HotkeyAction, HotkeyModifiers> binding in bindings) {
            if(binding.Value != modifiers) continue;
            if(!keyDown) {
                keyDown = true;
                action = binding.Key;
            }
            return true;
        }
        return false;
    }
}

/// <summary>
/// Low-Level-Tastatur-Hook (<c>WH_KEYBOARD_LL</c>) auf einem eigenen Thread mit eigener Nachrichtenschleife.
/// Windows ruft den Hook im Thread auf, der ihn installiert hat. Läge er im UI-Thread, würde jede längere Arbeit dort
/// (Aufnahme, Editor-Aufbau) den Hook blockieren: Windows überspringt ihn dann nach <c>LowLevelHooksTimeout</c>
/// (das Loslassen der Druck-Taste ginge verloren) und entfernt ihn unter Umständen stillschweigend.
/// Der Callback selbst macht keine UI-Arbeit; Aktionen gehen über <paramref name="pressed"/> (z. B. Dispatcher.BeginInvoke).
/// </summary>
public sealed class KeyboardHook : IDisposable {
    private const uint wmQuit = 0x0012;
    private const int vkShift = 0x10;
    private const int vkControl = 0x11;
    private const int vkMenu = 0x12;
    private const int vkLWin = 0x5B;
    private const int vkRWin = 0x5C;
    private const int startTimeoutMs = 5000;
    private const int stopTimeoutMs = 2000;

    private readonly PrintScreenFilter filter;
    private readonly Action<HotkeyAction> pressed;
    private readonly User32.LowLevelKeyboardProc proc;
    private readonly ManualResetEventSlim started = new ManualResetEventSlim(false);
    private readonly Thread thread;
    private IntPtr handle = IntPtr.Zero;
    private uint threadId;
    private int startError;
    private bool disposed;

    private KeyboardHook(PrintScreenFilter filter, Action<HotkeyAction> pressed) {
        this.filter = filter;
        this.pressed = pressed;
        proc = callback;
        thread = new Thread(run) {
            IsBackground = true,
            Name = "ScreenKeeper Tastatur-Hook"
        };
    }

    /// <summary>
    /// Startet den Hook-Thread und installiert den Hook. Null, wenn das nicht klappt (<paramref name="error"/> = Win32-Fehler).
    /// </summary>
    public static KeyboardHook? Start(PrintScreenFilter filter, Action<HotkeyAction> pressed, out int error) {
        KeyboardHook hook = new KeyboardHook(filter, pressed);
        hook.thread.Start();
        if(!hook.started.Wait(startTimeoutMs)) {
            error = -1;
            hook.Dispose();
            return null;
        }
        error = hook.startError;
        if(hook.handle == IntPtr.Zero) {
            hook.Dispose();
            return null;
        }
        return hook;
    }

    public void Dispose() {
        if(disposed) return;
        disposed = true;
        if(threadId != 0) User32.PostThreadMessage(threadId, wmQuit, IntPtr.Zero, IntPtr.Zero);
        if(thread.IsAlive && !thread.Join(stopTimeoutMs)) Log.Warn("Tastatur-Hook-Thread hat sich nicht rechtzeitig beendet");
        started.Dispose();
    }

    private void run() {
        try {
            threadId = Kernel32.GetCurrentThreadId();
            // Nachrichtenwarteschlange anlegen, bevor jemand WM_QUIT schickt
            User32.PeekMessage(out MSG message, IntPtr.Zero, 0, 0, User32.PM_NOREMOVE);
            handle = User32.SetWindowsHookEx(User32.WH_KEYBOARD_LL, proc, Kernel32.GetModuleHandle(null), 0);
            if(handle == IntPtr.Zero) startError = Marshal.GetLastWin32Error();
        } catch(Exception ex) {
            Log.Error(ex, "Tastatur-Hook konnte nicht gestartet werden");
            startError = -1;
        }
        started.Set();
        if(handle == IntPtr.Zero) return;
        try {
            while(User32.GetMessage(out MSG message, IntPtr.Zero, 0, 0) > 0) {
                // keine Fenster in diesem Thread: Nachrichten nur abholen (der Hook wird dabei aufgerufen)
            }
        } finally {
            User32.UnhookWindowsHookEx(handle);
            handle = IntPtr.Zero;
            GC.KeepAlive(proc);
        }
    }

    /// <summary>
    /// Muss sehr schnell zurückkehren: nur Tastenzustand lesen, entscheiden, Aktion weiterreichen.
    /// </summary>
    private IntPtr callback(int nCode, IntPtr wParam, IntPtr lParam) {
        if(nCode >= 0) {
            KBDLLHOOKSTRUCT data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int virtualKey = (int)data.vkCode;
            if(virtualKey == HotkeyBinding.VkPrintScreen) {
                int message = wParam.ToInt32();
                bool down = message == User32.WM_KEYDOWN || message == User32.WM_SYSKEYDOWN;
                if(filter.Process(virtualKey, down, down ? currentModifiers() : HotkeyModifiers.None, out HotkeyAction? action)) {
                    if(action.HasValue) {
                        try {
                            pressed(action.Value);
                        } catch(Exception ex) {
                            Log.Error(ex, "Hotkey-Aktion konnte nicht weitergereicht werden");
                        }
                    }
                    return new IntPtr(1);
                }
            }
        }
        return User32.CallNextHookEx(handle, nCode, wParam, lParam);
    }

    private static HotkeyModifiers currentModifiers() {
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        if(isDown(vkControl)) modifiers |= HotkeyModifiers.Ctrl;
        if(isDown(vkMenu)) modifiers |= HotkeyModifiers.Alt;
        if(isDown(vkShift)) modifiers |= HotkeyModifiers.Shift;
        if(isDown(vkLWin) || isDown(vkRWin)) modifiers |= HotkeyModifiers.Win;
        return modifiers;
    }

    private static bool isDown(int virtualKey) {
        return (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }
}
