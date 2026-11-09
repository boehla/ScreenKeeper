using System.Security.Principal;
using System.Windows.Interop;
using ScreenKeeper.Services.Native;

namespace ScreenKeeper.Services;

/// <summary>
/// Einzelinstanz je Benutzer über einen Mutex. Ein zweiter Start schickt die registrierte Fensternachricht
/// <c>ScreenKeeper.ShowHistory</c> als Broadcast; die erste Instanz empfängt sie über ein verstecktes Top-Level-Fenster.
/// </summary>
public sealed class SingleInstance : IDisposable {
    public static readonly uint ShowHistoryMessage = User32.RegisterWindowMessage("ScreenKeeper.ShowHistory");

    private Mutex? mutex;
    private bool ownsMutex;
    private HwndSource? listener;

    public event EventHandler? ShowHistoryRequested;

    public static string MutexName {
        get {
            string user;
            try {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) {
                    user = identity.User?.Value ?? Environment.UserName;
                }
            } catch(Exception ex) {
                Log.Error(ex, "Benutzer-SID nicht ermittelbar");
                user = Environment.UserName;
            }
            return $"Local\\ScreenKeeper.{user}";
        }
    }

    /// <summary>
    /// True, wenn dies die erste Instanz ist.
    /// </summary>
    public bool TryAcquire() {
        mutex = new Mutex(true, MutexName, out bool createdNew);
        if(createdNew) {
            ownsMutex = true;
            return true;
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

    private IntPtr wndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
        if(ShowHistoryMessage != 0 && (uint)msg == ShowHistoryMessage) {
            handled = true;
            ShowHistoryRequested?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }
}
