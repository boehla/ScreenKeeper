using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenKeeper.Resources;
using ScreenKeeper.Services.Native;

namespace ScreenKeeper.Services;

public enum HotkeyAction {
    /// <summary>Aufnahme + Editor</summary>
    CaptureEdit = 1,
    /// <summary>Aufnahme nur speichern</summary>
    CaptureSave = 2,
    /// <summary>History öffnen</summary>
    ShowHistory = 3
}

/// <summary>
/// Wie ein Hotkey gerade aktiv ist.
/// </summary>
public enum HotkeyState {
    /// <summary>Kein Hotkey eingestellt.</summary>
    Off,
    /// <summary>Über <c>RegisterHotKey</c> registriert.</summary>
    Registered,
    /// <summary>Druck-Kombination über den Tastatur-Hook (RegisterHotKey war nicht möglich).</summary>
    Hooked,
    /// <summary>Nicht registrierbar (z. B. von einem anderen Programm belegt).</summary>
    Failed,
    /// <summary>Text ist kein gültiger Hotkey.</summary>
    Invalid,
    /// <summary>Vorübergehend ausgesetzt (z. B. während im Einstellungsfenster ein Hotkey aufgezeichnet wird).</summary>
    Suspended
}

/// <summary>
/// Zustand eines Hotkeys; <see cref="Error"/> = Win32-Fehler beim Registrieren (z. B. 1409 = schon belegt).
/// </summary>
public sealed record HotkeyStatus(HotkeyState State, HotkeyBinding? Binding, int Error = 0);

/// <summary>
/// Ergebnis einer Probe-Registrierung.
/// </summary>
public enum HotkeyProbeResult {
    /// <summary>Frei (oder bereits von ScreenKeeper selbst registriert).</summary>
    Available,
    /// <summary>Von einem anderen Programm belegt (Fehler 1409).</summary>
    Taken,
    /// <summary>Aus einem anderen Grund nicht registrierbar.</summary>
    Failed
}

public interface IHotkeyService : IDisposable {
    event EventHandler<HotkeyAction>? HotkeyPressed;

    /// <summary>Zustand je Aktion nach dem letzten <see cref="Apply"/>.</summary>
    IReadOnlyDictionary<HotkeyAction, HotkeyStatus> Status { get; }

    /// <summary>
    /// Registriert die Hotkeys aus den Einstellungen neu. Liefert Hinweistexte für nicht registrierbare Hotkeys.
    /// </summary>
    IReadOnlyList<string> Apply(AppSettings settings);

    /// <summary>
    /// Prüft, ob sich <paramref name="binding"/> registrieren ließe (kurz registrieren und wieder freigeben).
    /// Bereits von ScreenKeeper registrierte Kombinationen gelten als frei. Im UI-Thread aufrufen.
    /// </summary>
    HotkeyProbeResult Probe(HotkeyBinding binding, out int error);

    /// <summary>Alle Hotkeys vorübergehend abmelden (z. B. zum Aufzeichnen einer Tastenkombination).</summary>
    void Suspend();

    /// <summary>Hotkeys nach <see cref="Suspend"/> wieder mit den zuletzt angewendeten Einstellungen registrieren.</summary>
    void Resume();
}

/// <summary>
/// Globale Hotkeys über <c>RegisterHotKey</c> an einem versteckten Fenster. Ist eine Druck-Kombination dort nicht
/// registrierbar (z. B. belegt), wird sie über einen Low-Level-Tastatur-Hook (<c>WH_KEYBOARD_LL</c>, eigener Thread,
/// siehe <see cref="KeyboardHook"/>) abgefangen – aber nur genau die konfigurierten Druck-Kombinationen.
/// </summary>
public sealed class HotkeyService : IHotkeyService {
    /// <summary>ERROR_HOTKEY_ALREADY_REGISTERED</summary>
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    private const int probeId = 0x0100;

    private readonly HwndSource window;
    private readonly Dispatcher dispatcher;
    private readonly Dictionary<HotkeyAction, HotkeyBinding> registered = new Dictionary<HotkeyAction, HotkeyBinding>();
    private readonly Dictionary<HotkeyAction, HotkeyBinding> hookBindings = new Dictionary<HotkeyAction, HotkeyBinding>();
    private readonly Dictionary<HotkeyAction, HotkeyStatus> status = new Dictionary<HotkeyAction, HotkeyStatus>();
    private AppSettings? lastSettings;
    private KeyboardHook? hook;
    private bool suspended;
    private bool disposed;

    public HotkeyService() {
        dispatcher = Dispatcher.CurrentDispatcher;
        HwndSourceParameters parameters = new HwndSourceParameters("ScreenKeeperHotkeys") {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            // Nachrichtenfenster (HWND_MESSAGE): unsichtbar, empfängt WM_HOTKEY
            ParentWindow = new IntPtr(-3)
        };
        window = new HwndSource(parameters);
        window.AddHook(wndProc);
    }

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    /// <summary>Hotkeys, die über den Tastatur-Hook statt RegisterHotKey laufen (für Diagnose/Tests).</summary>
    public IReadOnlyDictionary<HotkeyAction, HotkeyBinding> HookedBindings => hookBindings;

    public IReadOnlyDictionary<HotkeyAction, HotkeyStatus> Status => status;

    public IReadOnlyList<string> Apply(AppSettings settings) {
        lastSettings = settings;
        unregisterAll();
        status.Clear();
        List<string> problems = new List<string>();
        if(suspended) {
            foreach(HotkeyAction action in Enum.GetValues<HotkeyAction>()) status[action] = new HotkeyStatus(HotkeyState.Suspended, null);
            return problems;
        }
        register(HotkeyAction.CaptureEdit, settings.HotkeyCaptureEdit, problems);
        register(HotkeyAction.CaptureSave, settings.HotkeyCaptureSave, problems);
        register(HotkeyAction.ShowHistory, settings.HotkeyHistory, problems);
        if(hookBindings.Count > 0) installHook(problems);
        return problems;
    }

    public HotkeyProbeResult Probe(HotkeyBinding binding, out int error) {
        error = 0;
        if(registered.ContainsValue(binding)) return HotkeyProbeResult.Available;
        if(User32.RegisterHotKey(window.Handle, probeId, (uint)binding.Modifiers | User32.MOD_NOREPEAT, (uint)binding.VirtualKey)) {
            User32.UnregisterHotKey(window.Handle, probeId);
            return HotkeyProbeResult.Available;
        }
        error = Marshal.GetLastWin32Error();
        Log.Info($"Probe: Hotkey {binding} nicht registrierbar (Fehler {error})");
        return error == ErrorHotkeyAlreadyRegistered ? HotkeyProbeResult.Taken : HotkeyProbeResult.Failed;
    }

    public void Suspend() {
        if(suspended) return;
        suspended = true;
        unregisterAll();
        foreach(HotkeyAction action in status.Keys.ToList()) {
            HotkeyStatus old = status[action];
            if(old.State != HotkeyState.Off && old.State != HotkeyState.Invalid) status[action] = new HotkeyStatus(HotkeyState.Suspended, old.Binding);
        }
        Log.Info("Hotkeys ausgesetzt");
    }

    public void Resume() {
        if(!suspended) return;
        suspended = false;
        Log.Info("Hotkeys werden wieder registriert");
        if(lastSettings != null) Apply(lastSettings);
    }

    public void Dispose() {
        if(disposed) return;
        disposed = true;
        unregisterAll();
        window.RemoveHook(wndProc);
        window.Dispose();
    }

    private void register(HotkeyAction action, string text, List<string> problems) {
        if(string.IsNullOrWhiteSpace(text)) {
            status[action] = new HotkeyStatus(HotkeyState.Off, null);
            return;
        }
        if(!HotkeyBinding.TryParse(text, out HotkeyBinding? binding) || binding == null) {
            status[action] = new HotkeyStatus(HotkeyState.Invalid, null);
            problems.Add(Strings.Format(Strings.HintHotkeyInvalid, text));
            return;
        }
        int id = (int)action;
        if(User32.RegisterHotKey(window.Handle, id, (uint)binding.Modifiers | User32.MOD_NOREPEAT, (uint)binding.VirtualKey)) {
            registered[action] = binding;
            status[action] = new HotkeyStatus(HotkeyState.Registered, binding);
            Log.Info($"Hotkey {binding} für {action} registriert");
            return;
        }
        int error = Marshal.GetLastWin32Error();
        if(binding.IsPrintScreen) {
            Log.Info($"Hotkey {binding} für {action} nicht registrierbar (Fehler {error}) – Tastatur-Hook wird verwendet");
            hookBindings[action] = binding;
            status[action] = new HotkeyStatus(HotkeyState.Hooked, binding, error);
            return;
        }
        Log.Warn($"Hotkey {binding} für {action} nicht registrierbar (Fehler {error})");
        status[action] = new HotkeyStatus(HotkeyState.Failed, binding, error);
        problems.Add(Strings.Format(Strings.HintHotkeyFailed, binding.ToString()));
    }

    private void installHook(List<string> problems) {
        // Der Hook läuft in einem eigenen Thread; die Aktion wird asynchron im UI-Thread ausgeführt
        PrintScreenFilter filter = new PrintScreenFilter(hookBindings);
        hook = KeyboardHook.Start(filter, action => dispatcher.BeginInvoke(new Action(() => raise(action))), out int error);
        if(hook == null) {
            Log.Warn($"Tastatur-Hook konnte nicht installiert werden (Fehler {error})");
            foreach(KeyValuePair<HotkeyAction, HotkeyBinding> pair in hookBindings) {
                status[pair.Key] = new HotkeyStatus(HotkeyState.Failed, pair.Value, error);
                problems.Add(Strings.Format(Strings.HintHotkeyFailed, pair.Value.ToString()));
            }
            hookBindings.Clear();
            return;
        }
        Log.Info($"Tastatur-Hook installiert für {string.Join(", ", hookBindings.Select(p => $"{p.Value} ({p.Key})"))}");
    }

    private void unregisterAll() {
        foreach(HotkeyAction action in registered.Keys) User32.UnregisterHotKey(window.Handle, (int)action);
        registered.Clear();
        if(hook != null) {
            hook.Dispose();
            hook = null;
        }
        hookBindings.Clear();
    }

    private IntPtr wndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
        if(msg == User32.WM_HOTKEY) {
            int id = wParam.ToInt32();
            if(Enum.IsDefined(typeof(HotkeyAction), id)) {
                handled = true;
                raise((HotkeyAction)id);
            }
        }
        return IntPtr.Zero;
    }

    private void raise(HotkeyAction action) {
        try {
            HotkeyPressed?.Invoke(this, action);
        } catch(Exception ex) {
            Log.Error(ex, $"Fehler bei Hotkey-Aktion {action}");
        }
    }
}
