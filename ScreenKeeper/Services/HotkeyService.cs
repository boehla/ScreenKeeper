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
    ShowHistory = 3,
    /// <summary>Verzögerte Aufnahme (Countdown)</summary>
    CaptureDelayed = 4,
    /// <summary>Reines Druck an die Remotesitzung durchreichen (nur über den Tastatur-Hook, nur bei Remote-Fenster vorne)</summary>
    RemotePrintScreen = 5,
    /// <summary>Esc während des Countdowns (intern, nur über den Tastatur-Hook, nicht einstellbar)</summary>
    CancelDelayed = 6
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
    Suspended,
    /// <summary>Nur über den Tastatur-Hook, solange ein Remotedesktop-Fenster im Vordergrund ist (Durchreich-Hotkey).</summary>
    RemoteOnly
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
/// Mit <see cref="AppSettings.CaptureHotkeysInRemoteSessions"/> laufen zusätzlich alle registrierten Hotkeys über den
/// Hook, solange ein Remotedesktop-Fenster im Vordergrund ist (dort kommt WM_HOTKEY nicht an); außerhalb lässt der Hook
/// sie durch, damit sie nicht doppelt auslösen.
/// </summary>
public sealed class HotkeyService : IHotkeyService {
    /// <summary>ERROR_HOTKEY_ALREADY_REGISTERED</summary>
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    /// <summary>VK_ESCAPE</summary>
    public const int VkEscape = 0x1B;

    private const int probeId = 0x0100;

    private readonly HwndSource window;
    private readonly Dispatcher dispatcher;
    private readonly Dictionary<HotkeyAction, HotkeyBinding> registered = new Dictionary<HotkeyAction, HotkeyBinding>();
    private readonly Dictionary<HotkeyAction, HotkeyBinding> hookBindings = new Dictionary<HotkeyAction, HotkeyBinding>();
    private readonly Dictionary<HotkeyAction, HotkeyStatus> status = new Dictionary<HotkeyAction, HotkeyStatus>();
    private AppSettings? lastSettings;
    private HotkeyBinding? remotePrintScreen;
    private bool remoteEnabled;
    private IReadOnlyList<string> remoteClasses = Array.Empty<string>();
    private HotkeyFilter? filter;
    private KeyboardHook? hook;
    private bool cancelKeyActive;
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

    /// <summary>Hotkeys, die immer über den Tastatur-Hook statt RegisterHotKey laufen (für Diagnose/Tests).</summary>
    public IReadOnlyDictionary<HotkeyAction, HotkeyBinding> HookedBindings => hookBindings;

    /// <summary>Alle Kombinationen, die der Tastatur-Hook gerade kennt (für Diagnose/Tests).</summary>
    public IReadOnlyList<HookBinding> FilterBindings => filter?.Bindings ?? Array.Empty<HookBinding>();

    public IReadOnlyDictionary<HotkeyAction, HotkeyStatus> Status => status;

    public IReadOnlyList<string> Apply(AppSettings settings) {
        lastSettings = settings;
        unregisterAll();
        status.Clear();
        List<string> problems = new List<string>();
        if(suspended) {
            foreach(HotkeyAction action in configurableActions) status[action] = new HotkeyStatus(HotkeyState.Suspended, null);
            return problems;
        }
        remoteEnabled = settings.CaptureHotkeysInRemoteSessions;
        remoteClasses = RemoteSession.ParseWindowClasses(settings.RemoteSessionWindowClasses);
        register(HotkeyAction.CaptureEdit, settings.HotkeyCaptureEdit, problems);
        register(HotkeyAction.CaptureSave, settings.HotkeyCaptureSave, problems);
        register(HotkeyAction.ShowHistory, settings.HotkeyHistory, problems);
        register(HotkeyAction.CaptureDelayed, settings.HotkeyCaptureDelayed, problems);
        registerRemotePrintScreen(settings.HotkeyRemotePrintScreen, problems);
        startHook(problems);
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

    /// <summary>
    /// Esc für die Dauer des Countdowns abfangen (über den Tastatur-Hook, also auch ohne Fokus und im Remotedesktop;
    /// Esc wird dabei geschluckt) bzw. wieder freigeben. Löst <see cref="HotkeyAction.CancelDelayed"/> aus.
    /// </summary>
    public void SetCancelKeyActive(bool active) {
        if(cancelKeyActive == active) return;
        cancelKeyActive = active;
        if(suspended || disposed) return;
        List<HookBinding> bindings = buildHookBindings();
        if(filter != null && hook != null) {
            filter.SetBindings(bindings);
            hook.Update();
            return;
        }
        if(active) startHook(new List<string>());
    }

    public void Dispose() {
        if(disposed) return;
        disposed = true;
        unregisterAll();
        window.RemoveHook(wndProc);
        window.Dispose();
    }

    /// <summary>
    /// Welche Kombinationen der Tastatur-Hook kennt (UI-frei, testbar): fehlgeschlagene Druck-Kombinationen immer;
    /// mit Remote-Erfassung alle per RegisterHotKey registrierten und der Durchreich-Hotkey nur bei Remote-Fenster
    /// vorne (sonst feuert WM_HOTKEY – der Hook darf sie dann weder auslösen noch schlucken); Esc im Countdown immer.
    /// </summary>
    public static List<HookBinding> BuildHookBindings(IReadOnlyDictionary<HotkeyAction, HotkeyBinding> hooked, IReadOnlyDictionary<HotkeyAction, HotkeyBinding> registered,
        HotkeyBinding? remotePrintScreen, bool remoteEnabled, bool cancelKeyActive) {
        List<HookBinding> bindings = new List<HookBinding>();
        foreach(KeyValuePair<HotkeyAction, HotkeyBinding> pair in hooked) bindings.Add(new HookBinding(pair.Key, pair.Value, HotkeyScope.Always));
        if(remoteEnabled) {
            foreach(KeyValuePair<HotkeyAction, HotkeyBinding> pair in registered) bindings.Add(new HookBinding(pair.Key, pair.Value, HotkeyScope.RemoteOnly));
            if(remotePrintScreen != null) bindings.Add(new HookBinding(HotkeyAction.RemotePrintScreen, remotePrintScreen, HotkeyScope.RemoteOnly));
        }
        if(cancelKeyActive) bindings.Add(new HookBinding(HotkeyAction.CancelDelayed, new HotkeyBinding(HotkeyModifiers.None, VkEscape), HotkeyScope.Always));
        return bindings;
    }

    private static IEnumerable<HotkeyAction> configurableActions => Enum.GetValues<HotkeyAction>().Where(a => a != HotkeyAction.CancelDelayed);

    private List<HookBinding> buildHookBindings() {
        return BuildHookBindings(hookBindings, registered, remotePrintScreen, remoteEnabled && remoteClasses.Count > 0, cancelKeyActive);
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

    /// <summary>
    /// Durchreich-Hotkey: wird nie registriert, sondern nur vom Hook abgefangen – und nur, wenn die Remote-Erfassung an ist.
    /// </summary>
    private void registerRemotePrintScreen(string text, List<string> problems) {
        remotePrintScreen = null;
        if(string.IsNullOrWhiteSpace(text) || !remoteEnabled || remoteClasses.Count == 0) {
            status[HotkeyAction.RemotePrintScreen] = new HotkeyStatus(HotkeyState.Off, null);
            return;
        }
        if(!HotkeyBinding.TryParse(text, out HotkeyBinding? binding) || binding == null) {
            status[HotkeyAction.RemotePrintScreen] = new HotkeyStatus(HotkeyState.Invalid, null);
            problems.Add(Strings.Format(Strings.HintHotkeyInvalid, text));
            return;
        }
        remotePrintScreen = binding;
        status[HotkeyAction.RemotePrintScreen] = new HotkeyStatus(HotkeyState.RemoteOnly, binding);
        Log.Info($"Hotkey {binding} reicht im Remotedesktop Druck an die Sitzung durch");
    }

    private void startHook(List<string> problems) {
        List<HookBinding> bindings = buildHookBindings();
        if(bindings.Count == 0) return;
        filter = new HotkeyFilter(bindings);
        bool observeRemote = filter.HasRemoteBindings;
        if(!filter.NeedsHook && !observeRemote) {
            filter = null;
            return;
        }
        // Der Hook läuft in einem eigenen Thread; die Aktion wird asynchron im UI-Thread ausgeführt
        hook = KeyboardHook.Start(filter, observeRemote ? remoteClasses : Array.Empty<string>(), action => dispatcher.BeginInvoke(new Action(() => raise(action))), out int error);
        if(hook == null) {
            Log.Warn($"Tastatur-Hook konnte nicht installiert werden (Fehler {error})");
            foreach(KeyValuePair<HotkeyAction, HotkeyBinding> pair in hookBindings) {
                status[pair.Key] = new HotkeyStatus(HotkeyState.Failed, pair.Value, error);
                problems.Add(Strings.Format(Strings.HintHotkeyFailed, pair.Value.ToString()));
            }
            hookBindings.Clear();
            filter = null;
            return;
        }
        if(hookBindings.Count > 0) Log.Info($"Tastatur-Hook installiert für {string.Join(", ", hookBindings.Select(p => $"{p.Value} ({p.Key})"))}");
        if(observeRemote) Log.Info($"Remotedesktop-Erfassung aktiv für Fensterklasse(n) {string.Join(", ", remoteClasses)}");
    }

    private void unregisterAll() {
        foreach(HotkeyAction action in registered.Keys) User32.UnregisterHotKey(window.Handle, (int)action);
        registered.Clear();
        if(hook != null) {
            hook.Dispose();
            hook = null;
        }
        filter = null;
        hookBindings.Clear();
        remotePrintScreen = null;
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
