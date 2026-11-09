using System.Windows;
using ScreenKeeper.Models;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Abbildung Bildpixel ↔ Ansicht (DIPs der Zeichenfläche): Ansicht = Bild · Zoom + Pan.
/// Die eine zentrale Umrechnung für Maus → Bildpixel: floor((Ansicht − Pan) / Zoom).
/// </summary>
public sealed class ViewTransform {
    /// <summary>Toleranz gegen Rundungsfehler (z. B. 99,99999 statt 100 bei Zoom = 1 / DPI-Skalierung).</summary>
    public const double Epsilon = 1e-4;

    /// <summary>DIPs je Bildpixel.</summary>
    public double Zoom { get; set; } = 1;

    public double PanX { get; set; }

    public double PanY { get; set; }

    /// <summary>
    /// Kontinuierliche Bildkoordinate (Pixel 5 reicht von 5,0 bis 5,999…).
    /// </summary>
    public Point ToImage(Point view) {
        return new Point((view.X - PanX) / Zoom, (view.Y - PanY) / Zoom);
    }

    public PixelPoint ToPixel(Point view) {
        Point image = ToImage(view);
        return new PixelPoint(FloorPixel(image.X), FloorPixel(image.Y));
    }

    public Point ToView(double imageX, double imageY) {
        return new Point(imageX * Zoom + PanX, imageY * Zoom + PanY);
    }

    public Rect ToView(PixelRect rect) {
        Point topLeft = ToView(rect.X, rect.Y);
        return new Rect(topLeft.X, topLeft.Y, rect.Width * Zoom, rect.Height * Zoom);
    }

    /// <summary>
    /// Zoom ändern, wobei der Bildpunkt unter <paramref name="view"/> an seiner Stelle bleibt.
    /// </summary>
    public void ZoomAt(Point view, double newZoom) {
        Point image = ToImage(view);
        Zoom = newZoom;
        PanX = view.X - image.X * newZoom;
        PanY = view.Y - image.Y * newZoom;
    }

    /// <summary>
    /// Bild in der Ansicht zentrieren.
    /// </summary>
    public void Center(int imageWidth, int imageHeight, double viewWidth, double viewHeight) {
        PanX = Math.Round((viewWidth - imageWidth * Zoom) / 2);
        PanY = Math.Round((viewHeight - imageHeight * Zoom) / 2);
    }

    /// <summary>
    /// Zoom, mit dem das Bild vollständig in die Ansicht passt, begrenzt auf <paramref name="minZoom"/> … <paramref name="maxZoom"/>.
    /// </summary>
    public static double FitZoom(int imageWidth, int imageHeight, double viewWidth, double viewHeight, double minZoom, double maxZoom) {
        if(imageWidth <= 0 || imageHeight <= 0 || viewWidth <= 0 || viewHeight <= 0) return Math.Clamp(1, minZoom, maxZoom);
        double zoom = Math.Min(viewWidth / imageWidth, viewHeight / imageHeight);
        return Math.Clamp(zoom, minZoom, maxZoom);
    }

    public static int FloorPixel(double value) {
        return (int)Math.Floor(value + Epsilon);
    }
}
