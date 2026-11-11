using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;
using ScreenKeeper.Services.Native;

namespace ScreenKeeper.Views;

/// <summary>
/// Kleines Countdown-Overlay der verzögerten Aufnahme in der rechten unteren Ecke des Monitors mit dem Mauszeiger.
/// Oberstes Fenster, nimmt nie den Fokus (WS_EX_NOACTIVATE, ShowActivated=false), ist klick-durchlässig
/// (WS_EX_TRANSPARENT), nicht in der Taskleiste (WS_EX_TOOLWINDOW) und per <c>WDA_EXCLUDEFROMCAPTURE</c> von
/// Bildschirmaufnahmen ausgenommen. Vor der Aufnahme wird es zusätzlich ausgeblendet (<see cref="HideForCaptureAsync"/>).
/// </summary>
public partial class CountdownOverlay : Window {
    private const int marginPx = 24;
    private const int flushTimeoutMs = 500;

    public CountdownOverlay() {
        InitializeComponent();
        SourceInitialized += onSourceInitialized;
    }

    public void SetSeconds(int seconds) {
        secondsText.Text = Strings.Format(Strings.DelayDialogQuick, seconds);
    }

    /// <summary>
    /// Ausblenden und sicherstellen, dass es nicht mehr auf dem Bildschirm ist, bevor aufgenommen wird
    /// (Fenster unsichtbar und die Desktop-Komposition hat mindestens zwei neue Bilder ausgegeben).
    /// </summary>
    public async Task HideForCaptureAsync() {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        Hide();
        long start = Environment.TickCount64;
        while(handle != IntPtr.Zero && User32.IsWindowVisible(handle) && Environment.TickCount64 - start < flushTimeoutMs) await Task.Delay(10);
        Dwm.DwmFlush();
        await Task.Delay(30);
        Dwm.DwmFlush();
    }

    private void onSourceInitialized(object? sender, EventArgs e) {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if(handle == IntPtr.Zero) return;
        long style = User32.GetWindowLongPtr(handle, User32.GWL_EXSTYLE).ToInt64();
        style |= User32.WS_EX_NOACTIVATE | User32.WS_EX_TOOLWINDOW | User32.WS_EX_TRANSPARENT;
        User32.SetWindowLongPtr(handle, User32.GWL_EXSTYLE, new IntPtr(style));
        if(!User32.SetWindowDisplayAffinity(handle, User32.WDA_EXCLUDEFROMCAPTURE)) {
            Log.Warn($"Countdown-Overlay: Ausschluss aus Aufnahmen nicht möglich (Fehler {Marshal.GetLastWin32Error()}) – es wird vor der Aufnahme ausgeblendet");
        }
        placeAtCursorMonitor(handle);
    }

    /// <summary>
    /// Rechts unten in den Arbeitsbereich des Monitors mit dem Mauszeiger (physische Pixel). Erst auf den Monitor
    /// schieben (WPF passt die Größe an dessen DPI an), dann mit der tatsächlichen Größe in die Ecke.
    /// </summary>
    private static void placeAtCursorMonitor(IntPtr handle) {
        if(!User32.GetCursorPos(out POINT cursor)) return;
        IntPtr monitor = User32.MonitorFromPoint(cursor, User32.MONITOR_DEFAULTTONEAREST);
        MONITORINFOEX info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if(monitor == IntPtr.Zero || !User32.GetMonitorInfo(monitor, ref info)) return;
        RECT work = info.rcWork;
        uint flags = User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE;
        User32.SetWindowPos(handle, IntPtr.Zero, work.Left + marginPx, work.Top + marginPx, 0, 0, flags);
        if(!User32.GetWindowRect(handle, out RECT window)) return;
        int width = window.Right - window.Left;
        int height = window.Bottom - window.Top;
        User32.SetWindowPos(handle, IntPtr.Zero, work.Right - width - marginPx, work.Bottom - height - marginPx, 0, 0, flags);
    }
}
