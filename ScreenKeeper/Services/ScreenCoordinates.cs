using ScreenKeeper.Models;

namespace ScreenKeeper.Services;

/// <summary>
/// Umrechnung Bildschirmkoordinaten (physische Pixel) ↔ Bildpixel des Original-PNGs. Bildpixel (0, 0) ist die linke
/// obere Ecke des virtuellen Bildschirms; die liegt bei Monitoren links bzw. oberhalb des primären bei negativen
/// Bildschirmkoordinaten. Alle Stellen, die den Ursprung des virtuellen Bildschirms addieren oder abziehen, gehen hierüber.
/// </summary>
public static class ScreenCoordinates {
    public static PixelPoint ScreenToImage(int screenX, int screenY, PixelRect virtualScreen) {
        return new PixelPoint(screenX - virtualScreen.X, screenY - virtualScreen.Y);
    }

    public static PixelPoint ImageToScreen(PixelPoint image, PixelRect virtualScreen) {
        return new PixelPoint(image.X + virtualScreen.X, image.Y + virtualScreen.Y);
    }

    /// <summary>
    /// Rechteck in Bildschirmkoordinaten (Kanten wie bei <c>RECT</c>, rechts/unten exklusiv) → Bildpixel.
    /// </summary>
    public static PixelRect ScreenToImage(int left, int top, int right, int bottom, PixelRect virtualScreen) {
        return new PixelRect(left - virtualScreen.X, top - virtualScreen.Y, right - left, bottom - top);
    }

    /// <summary>
    /// Gleiche Monitor-Anordnung wie bei der Aufnahme? Gleicher virtueller Bildschirm und dieselben Monitore (Bereich in
    /// Bildpixeln und Skalierung; Reihenfolge egal). Dann kann der Editor 1:1 im Vollbild öffnen.
    /// </summary>
    public static bool IsSameLayout(PixelRect currentVirtualScreen, IReadOnlyList<MonitorInfo> currentMonitors, CaptureDocument document) {
        if(currentVirtualScreen != document.VirtualScreen) return false;
        if(currentMonitors.Count != document.Monitors.Count) return false;
        foreach(MonitorInfo saved in document.Monitors) {
            bool found = currentMonitors.Any(m => m.Bounds == saved.Bounds && Math.Abs(m.Scale - saved.Scale) < 0.01);
            if(!found) return false;
        }
        return true;
    }
}
