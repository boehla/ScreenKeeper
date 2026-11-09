using System.Runtime.InteropServices;

namespace ScreenKeeper.Services.Native;

/// <summary>
/// P/Invoke-Deklarationen aus shcore.dll.
/// </summary>
public static class ShCore {
    public const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
