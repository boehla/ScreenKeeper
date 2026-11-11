using System.Runtime.InteropServices;

namespace ScreenKeeper.Services.Native;

/// <summary>
/// P/Invoke-Deklarationen aus dwmapi.dll.
/// </summary>
public static class Dwm {
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    /// <summary>Wartet, bis die Desktop-Komposition das nächste Bild ausgegeben hat.</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmFlush();
}
