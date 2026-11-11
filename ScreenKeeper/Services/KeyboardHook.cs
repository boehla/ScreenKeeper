using System.Runtime.InteropServices;
using System.Text;
using ScreenKeeper.Services.Native;

namespace ScreenKeeper.Services;

/// <summary>
/// Low-Level-Tastatur-Hook (<c>WH_KEYBOARD_LL</c>) auf einem eigenen Thread mit eigener Nachrichtenschleife.
/// Windows ruft den Hook im Thread auf, der ihn installiert hat. Läge er im UI-Thread, würde jede längere Arbeit dort
/// (Aufnahme, Editor-Aufbau) den Hook blockieren: Windows überspringt ihn dann nach <c>LowLevelHooksTimeout</c>
/// (das Loslassen der Taste ginge verloren) und entfernt ihn unter Umständen stillschweigend.
/// Der Callback selbst macht keine UI-Arbeit; Aktionen gehen über <c>pressed</c> (z. B. Dispatcher.BeginInvoke).
/// <para>
/// Remotedesktop: mstsc setzt beim Aktivieren seines Fensters einen eigenen LL-Hook. LL-Hooks laufen in umgekehrter
/// Installationsreihenfolge, mstsc stünde also vor uns und reicht die Tasten nicht weiter (auch RegisterHotKey feuert
/// dann nicht). Deshalb beobachtet dieser Thread den Vordergrund (<c>SetWinEventHook</c>, Ereignis im selben Thread)
/// und setzt den Hook neu, sobald ein Remote-Fenster aktiv wird – sofort und nach 250 ms noch einmal, falls mstsc
/// seinen Hook erst danach setzt. Der LL-Hook ist nur installiert, solange <see cref="HotkeyFilter.NeedsHook"/> gilt.
/// </para>
/// </summary>
public sealed class KeyboardHook : IDisposable {
    /// <summary>Kennung in <c>dwExtraInfo</c> der eigenen SendInput-Ereignisse („SKPT“) – der Hook reicht sie unverändert weiter.</summary>
    public static readonly IntPtr InjectedMarker = new IntPtr(0x534B5054);

    private const uint wmQuit = 0x0012;
    private const uint wmReinstall = User32.WM_APP + 1;
    private const uint wmSendPrintScreen = User32.WM_APP + 2;
    private const uint wmUpdate = User32.WM_APP + 3;
    private const uint reinstallDelayMs = 250;
    private const int startTimeoutMs = 5000;
    private const int stopTimeoutMs = 2000;

    private static readonly HashSet<uint> checkedProcesses = new HashSet<uint>();
    private static bool? selfElevated;

    private readonly HotkeyFilter filter;
    private readonly IReadOnlyList<string> remoteClasses;
    private readonly Action<HotkeyAction> pressed;
    private readonly User32.LowLevelKeyboardProc proc;
    private readonly User32.WinEventProc winEventProc;
    private readonly ModifierTracker tracker = new ModifierTracker();
    private readonly ManualResetEventSlim started = new ManualResetEventSlim(false);
    private readonly Thread thread;
    private IntPtr handle = IntPtr.Zero;
    private IntPtr winEventHandle = IntPtr.Zero;
    private UIntPtr timerId = UIntPtr.Zero;
    private uint threadId;
    private int startError;
    private bool disposed;

    private KeyboardHook(HotkeyFilter filter, IReadOnlyList<string> remoteClasses, Action<HotkeyAction> pressed) {
        this.filter = filter;
        this.remoteClasses = remoteClasses;
        this.pressed = pressed;
        proc = callback;
        winEventProc = onWinEvent;
        thread = new Thread(run) {
            IsBackground = true,
            Name = "ScreenKeeper Tastatur-Hook"
        };
    }

    /// <summary>LL-Hook ist gerade installiert (für Diagnose).</summary>
    public bool IsInstalled => handle != IntPtr.Zero;

    /// <summary>
    /// Startet den Hook-Thread. Mit <paramref name="remoteClasses"/> (nicht leer) beobachtet er den Vordergrund auf
    /// Remote-Fenster. Null, wenn der sofort nötige Hook nicht installiert werden kann (<paramref name="error"/> = Win32-Fehler).
    /// </summary>
    public static KeyboardHook? Start(HotkeyFilter filter, IReadOnlyList<string> remoteClasses, Action<HotkeyAction> pressed, out int error) {
        KeyboardHook hook = new KeyboardHook(filter, remoteClasses, pressed);
        hook.thread.Start();
        if(!hook.started.Wait(startTimeoutMs)) {
            error = -1;
            hook.Dispose();
            return null;
        }
        error = hook.startError;
        if(error != 0) {
            hook.Dispose();
            return null;
        }
        return hook;
    }

    /// <summary>Nach geänderten Kombinationen: Hook je nach Bedarf installieren oder entfernen (asynchron im Hook-Thread).</summary>
    public void Update() {
        if(threadId != 0 && !disposed) User32.PostThreadMessage(threadId, wmUpdate, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose() {
        if(disposed) return;
        disposed = true;
        if(threadId != 0) User32.PostThreadMessage(threadId, wmQuit, IntPtr.Zero, IntPtr.Zero);
        if(thread.IsAlive && !thread.Join(stopTimeoutMs)) Log.Warn("Tastatur-Hook-Thread hat sich nicht rechtzeitig beendet");
        started.Dispose();
    }

    private void run() {
        bool failed = false;
        try {
            threadId = Kernel32.GetCurrentThreadId();
            // Nachrichtenwarteschlange anlegen, bevor jemand WM_QUIT schickt
            User32.PeekMessage(out MSG message, IntPtr.Zero, 0, 0, User32.PM_NOREMOVE);
            if(remoteClasses.Count > 0) {
                // Eigener Prozess wird bewusst nicht ausgeblendet: Wird ein ScreenKeeper-Fenster aktiv (Editor, Dialog),
                // ist das Remote-Fenster nicht mehr vorne und die Remote-Kombinationen müssen ruhen
                winEventHandle = User32.SetWinEventHook(User32.EVENT_SYSTEM_FOREGROUND, User32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc, 0, 0, User32.WINEVENT_OUTOFCONTEXT);
                if(winEventHandle == IntPtr.Zero) Log.Warn($"Vordergrund-Beobachtung für Remotedesktop nicht möglich (Fehler {Marshal.GetLastWin32Error()})");
                // Beim Start schon ein Remote-Fenster vorne?
                IntPtr foreground = User32.GetForegroundWindow();
                if(isRemoteWindow(foreground, out string className)) {
                    filter.RemoteActive = true;
                    Log.Info($"Remotedesktop-Fenster ({className}) bereits im Vordergrund – Tastatur-Hook wird gesetzt");
                    checkElevation(foreground);
                }
            }
            if(filter.NeedsHook && !install()) failed = true;
        } catch(Exception ex) {
            Log.Error(ex, "Tastatur-Hook konnte nicht gestartet werden");
            startError = -1;
            failed = true;
        }
        started.Set();
        try {
            if(failed) return;
            while(User32.GetMessage(out MSG message, IntPtr.Zero, 0, 0) > 0) {
                // keine Fenster in diesem Thread: Nachrichten selbst auswerten (der Hook wird beim Abholen aufgerufen)
                handleMessage(message);
            }
        } finally {
            if(timerId != UIntPtr.Zero) User32.KillTimer(IntPtr.Zero, timerId);
            uninstall();
            if(winEventHandle != IntPtr.Zero) User32.UnhookWinEvent(winEventHandle);
            winEventHandle = IntPtr.Zero;
            GC.KeepAlive(proc);
            GC.KeepAlive(winEventProc);
        }
    }

    private void handleMessage(MSG message) {
        try {
            switch(message.message) {
                case wmReinstall:
                    reinstall();
                    break;
                case wmUpdate:
                    updateHook();
                    break;
                case wmSendPrintScreen:
                    sendPrintScreen();
                    break;
                case User32.WM_TIMER:
                    if(timerId != UIntPtr.Zero && message.wParam == (IntPtr)(long)timerId.ToUInt64()) {
                        User32.KillTimer(IntPtr.Zero, timerId);
                        timerId = UIntPtr.Zero;
                        reinstall();
                    }
                    break;
            }
        } catch(Exception ex) {
            Log.Error(ex, "Fehler im Tastatur-Hook-Thread");
        }
    }

    private bool install() {
        if(handle != IntPtr.Zero) return true;
        handle = User32.SetWindowsHookEx(User32.WH_KEYBOARD_LL, proc, Kernel32.GetModuleHandle(null), 0);
        if(handle == IntPtr.Zero) {
            startError = Marshal.GetLastWin32Error();
            if(startError == 0) startError = -1;
            Log.Warn($"Tastatur-Hook konnte nicht installiert werden (Fehler {startError})");
            return false;
        }
        return true;
    }

    private void uninstall() {
        if(handle == IntPtr.Zero) return;
        User32.UnhookWindowsHookEx(handle);
        handle = IntPtr.Zero;
    }

    /// <summary>
    /// Entfernen und neu setzen: Der neue Hook steht danach vor allen anderen (auch vor dem von mstsc). Erst entfernen,
    /// dann setzen – zwei eigene Hooks gleichzeitig würden dieselbe Taste zweimal sehen.
    /// </summary>
    private void reinstall() {
        if(!filter.NeedsHook) {
            uninstall();
            return;
        }
        bool wasInstalled = handle != IntPtr.Zero;
        uninstall();
        if(!wasInstalled) tracker.Clear();
        install();
    }

    private void updateHook() {
        if(filter.NeedsHook) {
            if(handle == IntPtr.Zero) {
                tracker.Clear();
                install();
            }
        } else {
            uninstall();
        }
    }

    private void onWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime) {
        try {
            if(eventType != User32.EVENT_SYSTEM_FOREGROUND) return;
            bool remote = isRemoteWindow(hwnd, out string className);
            bool was = filter.RemoteActive;
            filter.RemoteActive = remote;
            if(remote) {
                if(!was) Log.Info($"Remotedesktop-Fenster ({className}) im Vordergrund – Tastatur-Hook wird neu gesetzt");
                checkElevation(hwnd);
                User32.PostThreadMessage(threadId, wmReinstall, IntPtr.Zero, IntPtr.Zero);
                // zweites Mal, falls mstsc seinen Hook erst nach der Aktivierung setzt
                if(timerId != UIntPtr.Zero) User32.KillTimer(IntPtr.Zero, timerId);
                timerId = User32.SetTimer(IntPtr.Zero, UIntPtr.Zero, reinstallDelayMs, IntPtr.Zero);
            } else if(was) {
                Log.Info("Remotedesktop-Fenster nicht mehr im Vordergrund");
                User32.PostThreadMessage(threadId, wmUpdate, IntPtr.Zero, IntPtr.Zero);
            }
        } catch(Exception ex) {
            Log.Error(ex, "Fehler bei der Vordergrund-Beobachtung");
        }
    }

    private bool isRemoteWindow(IntPtr hwnd, out string className) {
        className = "";
        if(hwnd == IntPtr.Zero) return false;
        StringBuilder buffer = new StringBuilder(256);
        int length = User32.GetClassName(hwnd, buffer, buffer.Capacity);
        if(length <= 0) return false;
        className = buffer.ToString();
        return RemoteSession.IsRemoteWindowClass(className, remoteClasses);
    }

    /// <summary>
    /// UIPI: Läuft das Remote-Fenster mit Administratorrechten, ScreenKeeper aber nicht, bekommt der Hook dort keine
    /// Tasten. Einmal je Prozess im Protokoll vermerken.
    /// </summary>
    private static void checkElevation(IntPtr hwnd) {
        User32.GetWindowThreadProcessId(hwnd, out uint processId);
        if(processId == 0) return;
        lock(checkedProcesses) {
            if(!checkedProcesses.Add(processId)) return;
            selfElevated ??= ProcessElevation.IsCurrentElevated() ?? false;
        }
        if(selfElevated == true) return;
        bool? elevated = ProcessElevation.IsElevated(processId);
        if(elevated == true) {
            Log.Warn($"Das Remotedesktop-Fenster (Prozess {processId}) läuft mit Administratorrechten, ScreenKeeper nicht – Hotkeys kommen dort nicht an. Remotedesktop ohne oder ScreenKeeper ebenfalls mit Administratorrechten starten.");
        } else if(elevated == null) {
            Log.Info($"Rechte des Remotedesktop-Prozesses {processId} nicht feststellbar – läuft er als Administrator, kommen Hotkeys dort nicht an");
        }
    }

    /// <summary>
    /// Reines Druck an die Remotesitzung schicken (außerhalb des Hook-Callbacks). Eigene Ereignisse tragen
    /// <see cref="InjectedMarker"/> und gehen im eigenen Hook unverändert weiter.
    /// </summary>
    private void sendPrintScreen() {
        HashSet<int> held = new HashSet<int>(tracker.Held);
        foreach(int key in ModifierTracker.ModifierKeys) {
            if((User32.GetAsyncKeyState(key) & 0x8000) != 0) held.Add(key);
        }
        IReadOnlyList<KeyStroke> strokes = RemoteKeySequence.Build(held);
        INPUT[] inputs = new INPUT[strokes.Count];
        for(int i = 0; i < strokes.Count; i++) {
            KeyStroke stroke = strokes[i];
            uint flags = 0;
            if(stroke.Extended) flags |= User32.KEYEVENTF_EXTENDEDKEY;
            if(stroke.Up) flags |= User32.KEYEVENTF_KEYUP;
            inputs[i] = new INPUT {
                type = User32.INPUT_KEYBOARD,
                u = new INPUTUNION {
                    ki = new KEYBDINPUT {
                        wVk = (ushort)stroke.VirtualKey,
                        wScan = stroke.ScanCode,
                        dwFlags = flags,
                        dwExtraInfo = InjectedMarker
                    }
                }
            };
        }
        uint sent = User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if(sent != inputs.Length) {
            Log.Warn($"Druck an die Remotesitzung: nur {sent} von {inputs.Length} Ereignissen gesendet (Fehler {Marshal.GetLastWin32Error()})");
        } else {
            Log.Info($"Druck an die Remotesitzung durchgereicht ({held.Count} Zusatztaste(n) kurz losgelassen)");
        }
    }

    /// <summary>
    /// Muss sehr schnell zurückkehren: nur Tastenzustand lesen, entscheiden, Aktion weiterreichen.
    /// </summary>
    private IntPtr callback(int nCode, IntPtr wParam, IntPtr lParam) {
        if(nCode >= 0) {
            KBDLLHOOKSTRUCT data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int virtualKey = (int)data.vkCode;
            int message = wParam.ToInt32();
            bool down = message == User32.WM_KEYDOWN || message == User32.WM_SYSKEYDOWN;
            if(ModifierTracker.IsModifier(virtualKey)) {
                tracker.Update(virtualKey, down);
            } else if(data.dwExtraInfo != InjectedMarker) {
                if(filter.Process(virtualKey, down, down ? currentModifiers() : HotkeyModifiers.None, out HotkeyAction? action)) {
                    if(action.HasValue) dispatch(action.Value);
                    return new IntPtr(1);
                }
            }
        }
        return User32.CallNextHookEx(handle, nCode, wParam, lParam);
    }

    private void dispatch(HotkeyAction action) {
        try {
            if(action == HotkeyAction.RemotePrintScreen) {
                // SendInput nicht im Callback: erst zurückkehren, dann über die eigene Nachrichtenschleife senden
                User32.PostThreadMessage(threadId, wmSendPrintScreen, IntPtr.Zero, IntPtr.Zero);
                return;
            }
            pressed(action);
        } catch(Exception ex) {
            Log.Error(ex, "Hotkey-Aktion konnte nicht weitergereicht werden");
        }
    }

    /// <summary>
    /// Zusatztasten laut <c>GetAsyncKeyState</c>; im Remote-Fall zusätzlich die selbst mitverfolgten (mstsc schluckt sie
    /// dort, dann kennt der lokale Tastenzustand sie nicht).
    /// </summary>
    private HotkeyModifiers currentModifiers() {
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        if(isDown(0x11)) modifiers |= HotkeyModifiers.Ctrl;
        if(isDown(0x12)) modifiers |= HotkeyModifiers.Alt;
        if(isDown(0x10)) modifiers |= HotkeyModifiers.Shift;
        if(isDown(ModifierTracker.VkLWin) || isDown(ModifierTracker.VkRWin)) modifiers |= HotkeyModifiers.Win;
        if(filter.RemoteActive) modifiers |= tracker.Modifiers;
        return modifiers;
    }

    private static bool isDown(int virtualKey) {
        return (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }
}
