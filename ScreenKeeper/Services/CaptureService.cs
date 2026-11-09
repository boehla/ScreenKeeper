using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services.Native;

namespace ScreenKeeper.Services;

/// <summary>
/// Ergebnis einer Aufnahme: das unveränderliche Original (Bgra32, 96 dpi, eingefroren) und das Dokument mit Metadaten.
/// </summary>
public sealed record CaptureResult(BitmapSource Bitmap, CaptureDocument Document) {
    /// <summary>Dauer der einzelnen Schritte (für das Protokoll).</summary>
    public string Timing { get; init; } = "";
}

public interface ICaptureService {
    /// <summary>Nimmt den gesamten virtuellen Bildschirm ohne Mauszeiger auf und erfasst Monitore, Zeiger und Fenster.</summary>
    CaptureResult Capture();

    /// <summary>Virtueller Bildschirm in Bildschirmkoordinaten (physische Pixel).</summary>
    PixelRect GetVirtualScreen();

    /// <summary>Aktuelle Monitore, Bounds relativ zu <paramref name="virtualScreen"/> (also in Bildpixeln).</summary>
    List<MonitorInfo> GetMonitors(PixelRect virtualScreen);

    /// <summary>Entspricht die aktuelle Monitor-Anordnung der des Dokuments (dann Vollbild-Editor möglich)?</summary>
    bool IsSameMonitorLayout(CaptureDocument document);
}

/// <summary>
/// Bildschirmaufnahme per GDI (BitBlt) in physischen Pixeln (der Prozess ist PerMonitorV2).
/// </summary>
public sealed class CaptureService : ICaptureService {
    public CaptureResult Capture() {
        DateTimeOffset now = DateTimeOffset.Now;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        PixelRect virtualScreen = GetVirtualScreen();
        Stopwatch watch = Stopwatch.StartNew();

        // Zeiger und Vordergrundfenster zuerst: Zustand im Moment des Tastendrucks
        IntPtr foregroundWindow = User32.GetForegroundWindow();
        CursorInfo? cursor = captureCursor(virtualScreen);
        long cursorMs = watch.ElapsedMilliseconds;
        BitmapSource bitmap = captureScreen(virtualScreen);
        long screenMs = watch.ElapsedMilliseconds;
        List<MonitorInfo> monitors = GetMonitors(virtualScreen);
        Dictionary<uint, string> processNames = new Dictionary<uint, string>();
        List<WindowInfo> windows = captureWindows(virtualScreen, foregroundWindow, processNames);
        ForegroundInfo? foreground = captureForeground(foregroundWindow, processNames);
        long windowsMs = watch.ElapsedMilliseconds;

        CaptureDocument document = new CaptureDocument {
            CapturedAt = now,
            VirtualScreen = virtualScreen,
            Monitors = monitors,
            Windows = windows,
            Foreground = foreground,
            Cursor = cursor
        };
        return new CaptureResult(bitmap, document) {
            Timing = $"Zeiger {cursorMs} ms, Bild {screenMs - cursorMs} ms, Monitore/Fenster {windowsMs - screenMs} ms"
        };
    }

    public PixelRect GetVirtualScreen() {
        return new PixelRect(
            User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN),
            User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN),
            User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN),
            User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN));
    }

    public List<MonitorInfo> GetMonitors(PixelRect virtualScreen) {
        List<MonitorInfo> monitors = new List<MonitorInfo>();
        User32.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) => {
            MONITORINFOEX info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = "" };
            if(!User32.GetMonitorInfo(hMonitor, ref info)) return true;
            double scale = 1.0;
            if(ShCore.GetDpiForMonitor(hMonitor, ShCore.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0 && dpiX > 0) {
                scale = dpiX / 96.0;
            }
            monitors.Add(new MonitorInfo {
                Device = info.szDevice ?? "",
                Bounds = toImageRect(info.rcMonitor, virtualScreen),
                Primary = (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0,
                Scale = scale
            });
            return true;
        };
        User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return monitors;
    }

    public bool IsSameMonitorLayout(CaptureDocument document) {
        PixelRect virtualScreen = GetVirtualScreen();
        return ScreenCoordinates.IsSameLayout(virtualScreen, GetMonitors(virtualScreen), document);
    }

    private static BitmapSource captureScreen(PixelRect virtualScreen) {
        int width = virtualScreen.Width;
        int height = virtualScreen.Height;
        IntPtr screenDc = User32.GetDC(IntPtr.Zero);
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr oldObject = IntPtr.Zero;
        try {
            memoryDc = Gdi32.CreateCompatibleDC(screenDc);
            BITMAPINFOHEADER header = BITMAPINFOHEADER.TopDown(width, height, 32);
            dib = Gdi32.CreateDIBSection(screenDc, ref header, Gdi32.DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            if(dib == IntPtr.Zero || bits == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDIBSection fehlgeschlagen");
            oldObject = Gdi32.SelectObject(memoryDc, dib);
            if(!Gdi32.BitBlt(memoryDc, 0, 0, width, height, screenDc, virtualScreen.X, virtualScreen.Y, Gdi32.SRCCOPY | Gdi32.CAPTUREBLT)) {
                // Typisch: Bildschirm gesperrt, sicherer Desktop (UAC) aktiv oder Remotedesktop-Fenster minimiert
                Win32Exception error = new Win32Exception(Marshal.GetLastWin32Error());
                throw new InvalidOperationException(Strings.Format(Strings.ErrorScreenUnavailable, error.NativeErrorCode), error);
            }
            Gdi32.GdiFlush();

            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            // Der Alphakanal ist nach BitBlt undefiniert → deckend setzen
            Span<uint> values = MemoryMarshal.Cast<byte, uint>(pixels.AsSpan());
            for(int i = 0; i < values.Length; i++) values[i] |= 0xFF000000;

            BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();
            return bitmap;
        } finally {
            if(oldObject != IntPtr.Zero) Gdi32.SelectObject(memoryDc, oldObject);
            if(dib != IntPtr.Zero) Gdi32.DeleteObject(dib);
            if(memoryDc != IntPtr.Zero) Gdi32.DeleteDC(memoryDc);
            User32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static CursorInfo? captureCursor(PixelRect virtualScreen) {
        try {
            CURSORINFO info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            if(!User32.GetCursorInfo(ref info)) {
                Log.Warn($"GetCursorInfo fehlgeschlagen (Fehler {Marshal.GetLastWin32Error()})");
                return null;
            }
            PixelPoint position = ScreenCoordinates.ScreenToImage(info.ptScreenPos.X, info.ptScreenPos.Y, virtualScreen);
            CursorInfo result = new CursorInfo {
                X = position.X,
                Y = position.Y,
                CapturedVisible = (info.flags & User32.CURSOR_SHOWING) != 0
            };
            CursorImage? image = CursorImage.FromHandle(info.hCursor);
            if(image != null) {
                result.Png = Convert.ToBase64String(image.ToPng());
                result.HotspotX = image.HotspotX;
                result.HotspotY = image.HotspotY;
            }
            return result;
        } catch(Exception ex) {
            Log.Error(ex, "Mauszeiger konnte nicht erfasst werden");
            return null;
        }
    }

    /// <summary>
    /// Titel und Prozess des Vordergrundfensters; null, wenn keins aktiv ist (z. B. gesperrter Bildschirm).
    /// </summary>
    private static ForegroundInfo? captureForeground(IntPtr hwnd, Dictionary<uint, string> processNames) {
        if(hwnd == IntPtr.Zero) return null;
        try {
            User32.GetWindowThreadProcessId(hwnd, out uint processId);
            return new ForegroundInfo {
                Title = getWindowTitle(hwnd),
                Process = processId != 0 ? getProcessName(processId, processNames) : ""
            };
        } catch(Exception ex) {
            Log.Error(ex, "Vordergrundfenster konnte nicht gelesen werden");
            return null;
        }
    }

    private static List<WindowInfo> captureWindows(PixelRect virtualScreen, IntPtr foregroundWindow, Dictionary<uint, string> processNames) {
        List<WindowInfo> windows = new List<WindowInfo>();
        uint ownProcessId = (uint)Environment.ProcessId;
        int rectSize = Marshal.SizeOf<RECT>();
        User32.EnumWindowsProc callback = (IntPtr hwnd, IntPtr data) => {
            try {
                if(!User32.IsWindowVisible(hwnd) || User32.IsIconic(hwnd)) return true;
                if(Dwm.DwmGetWindowAttribute(hwnd, Dwm.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                long exStyle = User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE).ToInt64();
                if((exStyle & User32.WS_EX_TRANSPARENT) != 0) return true;
                User32.GetWindowThreadProcessId(hwnd, out uint processId);
                if(processId == ownProcessId) return true;

                if(Dwm.DwmGetWindowAttribute(hwnd, Dwm.DWMWA_EXTENDED_FRAME_BOUNDS, out RECT rect, rectSize) != 0) {
                    if(!User32.GetWindowRect(hwnd, out rect)) return true;
                }
                PixelRect bounds = toImageRect(rect, virtualScreen);
                if(bounds.IsEmpty) return true;
                PixelRect screenArea = new PixelRect(0, 0, virtualScreen.Width, virtualScreen.Height);
                if(bounds.Intersect(screenArea).IsEmpty) return true;

                windows.Add(new WindowInfo {
                    Title = getWindowTitle(hwnd),
                    Process = getProcessName(processId, processNames),
                    Bounds = bounds,
                    Z = windows.Count,
                    Foreground = hwnd == foregroundWindow
                });
            } catch(Exception ex) {
                Log.Error(ex, "Fensterdaten konnten nicht gelesen werden");
            }
            return true;
        };
        User32.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return windows;
    }

    private static string getWindowTitle(IntPtr hwnd) {
        int length = User32.GetWindowTextLength(hwnd);
        if(length <= 0) return "";
        StringBuilder text = new StringBuilder(length + 1);
        User32.GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    private static string getProcessName(uint processId, Dictionary<uint, string> cache) {
        if(cache.TryGetValue(processId, out string? cached)) return cached;
        string name = "";
        IntPtr process = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if(process != IntPtr.Zero) {
            try {
                StringBuilder path = new StringBuilder(1024);
                uint size = (uint)path.Capacity;
                if(Kernel32.QueryFullProcessImageName(process, 0, path, ref size)) {
                    name = Path.GetFileNameWithoutExtension(path.ToString());
                }
            } finally {
                Kernel32.CloseHandle(process);
            }
        }
        cache[processId] = name;
        return name;
    }

    private static PixelRect toImageRect(RECT rect, PixelRect virtualScreen) {
        return ScreenCoordinates.ScreenToImage(rect.Left, rect.Top, rect.Right, rect.Bottom, virtualScreen);
    }
}
