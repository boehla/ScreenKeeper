namespace ScreenKeeper.Services.Native;

/// <summary>
/// Holt ein Fenster zuverlässig in den Vordergrund. Windows verweigert <c>SetForegroundWindow</c>, wenn ein anderer
/// Prozess den Fokus hat; mit <c>AttachThreadInput</c> an den Thread des Vordergrundfensters klappt es trotzdem.
/// </summary>
public static class WindowActivation {
    public static void ForceForeground(IntPtr handle) {
        if(handle == IntPtr.Zero) return;
        IntPtr foreground = User32.GetForegroundWindow();
        if(foreground == handle) return;
        uint ownThread = Kernel32.GetCurrentThreadId();
        uint foregroundThread = foreground == IntPtr.Zero ? 0 : User32.GetWindowThreadProcessId(foreground, out _);
        bool attached = foregroundThread != 0 && foregroundThread != ownThread && User32.AttachThreadInput(ownThread, foregroundThread, true);
        try {
            User32.BringWindowToTop(handle);
            User32.SetForegroundWindow(handle);
        } finally {
            if(attached) User32.AttachThreadInput(ownThread, foregroundThread, false);
        }
    }
}
