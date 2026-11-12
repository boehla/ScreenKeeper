using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Interop;
using ScreenKeeper.Services.Native;

namespace ScreenKeeper.Services;

/// <summary>
/// Einzelinstanz je Benutzer über einen Mutex. Ein zweiter Start schickt die registrierte Fensternachricht
/// <c>ScreenKeeper.ShowHistory</c> als Broadcast; die erste Instanz empfängt sie über ein verstecktes Top-Level-Fenster.
/// Erhöhte (Autostart mit Adminrechten) und normale Instanz desselben Benutzers sehen denselben Mutex.
/// </summary>
public sealed class SingleInstance : IDisposable {
    public static readonly uint ShowHistoryMessage = User32.RegisterWindowMessage("ScreenKeeper.ShowHistory");

    private readonly string mutexName;
    private Mutex? mutex;
    private bool ownsMutex;
    private HwndSource? listener;

    /// <param name="mutexName">Nur für Tests; Standard ist <see cref="MutexName"/>.</param>
    public SingleInstance(string? mutexName = null) {
        this.mutexName = mutexName ?? MutexName;
    }

    public event EventHandler? ShowHistoryRequested;

    public static string MutexName {
        get {
            string user = currentUserSid() ?? Environment.UserName;
            return $"Local\\ScreenKeeper.{user}";
        }
    }

    /// <summary>
    /// Zugriff für den Benutzer, SYSTEM und Administratoren: Legt die erhöhte Instanz den Mutex an, kann die normale ihn
    /// trotzdem öffnen (die Standard-DACL eines erhöhten Prozesses gilt nur für Administratoren). Eine Integritätsmarke
    /// „mittel“ (<c>S:(ML;;NW;;;ME)</c>) lehnt <see cref="MutexAcl"/> mit „Privileg fehlt“ ab – bleibt der Zugriff
    /// deshalb verweigert, wertet <see cref="TryAcquire"/> das als „läuft bereits“.
    /// </summary>
    public static string BuildMutexSddl(string sid) {
        return $"D:(A;;GA;;;{sid})(A;;GA;;;SY)(A;;GA;;;BA)";
    }

    /// <summary>
    /// True, wenn dies die erste Instanz ist.
    /// </summary>
    public bool TryAcquire() {
        try {
            mutex = createMutex(mutexName, out bool createdNew);
            if(createdNew) {
                ownsMutex = true;
                return true;
            }
        } catch(UnauthorizedAccessException ex) {
            // Mutex einer Instanz mit anderen Rechten (z. B. eine ältere erhöhte Version ohne passende ACL)
            Log.Warn($"Mutex nicht zugänglich, eine Instanz läuft bereits: {ex.Message}");
            mutex = null;
            return false;
        }
        try {
            ownsMutex = mutex.WaitOne(0);
        } catch(AbandonedMutexException ex) {
            // Vorherige Instanz ist abgestürzt
            Log.Warn($"Verwaister Mutex übernommen: {ex.Message}");
            ownsMutex = true;
        }
        return ownsMutex;
    }

    /// <summary>Läuft schon eine Instanz? (Nur nachsehen, nichts übernehmen.)</summary>
    public static bool IsRunning(string? mutexName = null) {
        try {
            if(!Mutex.TryOpenExisting(mutexName ?? MutexName, out Mutex? existing)) return false;
            existing.Dispose();
            return true;
        } catch(UnauthorizedAccessException) {
            return true;
        }
    }

    /// <summary>
    /// Bittet die laufende Instanz, die History zu zeigen.
    /// </summary>
    public static void SignalFirstInstance() {
        User32.AllowSetForegroundWindow(User32.ASFW_ANY);
        User32.PostMessage(User32.HWND_BROADCAST, ShowHistoryMessage, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Erzeugt das versteckte Empfangsfenster (Top-Level, damit es Broadcasts erhält).
    /// </summary>
    public void StartListening() {
        HwndSourceParameters parameters = new HwndSourceParameters("ScreenKeeperInstance") {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        listener = new HwndSource(parameters);
        listener.AddHook(wndProc);
        // Läuft diese Instanz erhöht, blockiert UIPI sonst die Nachricht eines normal gestarteten zweiten Starts
        if(ShowHistoryMessage != 0 && !User32.ChangeWindowMessageFilterEx(listener.Handle, ShowHistoryMessage, User32.MSGFLT_ALLOW, IntPtr.Zero)) {
            Log.Warn($"ChangeWindowMessageFilterEx fehlgeschlagen (Fehler {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
        }
    }

    public void Dispose() {
        if(listener != null) {
            listener.RemoveHook(wndProc);
            listener.Dispose();
            listener = null;
        }
        if(mutex != null) {
            if(ownsMutex) mutex.ReleaseMutex();
            mutex.Dispose();
            mutex = null;
        }
    }

    private static Mutex createMutex(string name, out bool createdNew) {
        string? sid = currentUserSid();
        if(sid == null) return new Mutex(true, name, out createdNew);
        MutexSecurity security = new MutexSecurity();
        security.SetSecurityDescriptorSddlForm(BuildMutexSddl(sid));
        return MutexAcl.Create(true, name, out createdNew, security);
    }

    private static string? currentUserSid() {
        try {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) {
                return identity.User?.Value;
            }
        } catch(Exception ex) {
            Log.Error(ex, "Benutzer-SID nicht ermittelbar");
            return null;
        }
    }

    private IntPtr wndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
        if(ShowHistoryMessage != 0 && (uint)msg == ShowHistoryMessage) {
            handled = true;
            ShowHistoryRequested?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }
}
