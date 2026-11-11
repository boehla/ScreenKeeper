using System.Windows;
using System.Windows.Media;

namespace ScreenKeeper.Views;

/// <summary>
/// Hilfslinien beim Wählen des Bereichs: je ein waagerechter und ein senkrechter, 1 Gerätepixel breiter Strich durch
/// den Zielpixel über die ganze Fläche (im Vollbild also über alle Monitore). Halbtransparent weiß mit dunklen
/// Strichen, damit die Linien auf hellem wie dunklem Untergrund dezent sichtbar bleiben. Zeichnet in Ansichts-DIPs
/// (nicht gezoomt) und liegt nur über der Anzeige – nie im ausgegebenen Bild.
/// </summary>
public sealed class CrosshairOverlay : FrameworkElement {
    private static readonly Brush lightBrush = freeze(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush darkBrush = freeze(new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)));

    private Point? center;
    private double deviceScale = 1;

    public CrosshairOverlay() {
        IsHitTestVisible = false;
        Focusable = false;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    /// <param name="center">Mitte des Zielpixels in Ansichts-DIPs; null = keine Hilfslinien</param>
    /// <param name="deviceScale">Gerätepixel je DIP</param>
    public void Update(Point? center, double deviceScale) {
        if(center == this.center && deviceScale == this.deviceScale) return;
        this.center = center;
        this.deviceScale = deviceScale;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc) {
        if(!center.HasValue) return;
        double px = 1 / deviceScale;
        // auf den Gerätepixel ausrichten, in dem die Mitte des Zielpixels liegt
        double x = Math.Floor(center.Value.X * deviceScale) / deviceScale + px / 2;
        double y = Math.Floor(center.Value.Y * deviceScale) / deviceScale + px / 2;
        Pen light = new Pen(lightBrush, px);
        Pen dashed = new Pen(darkBrush, px) { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) };
        if(y >= 0 && y <= ActualHeight) {
            dc.DrawLine(light, new Point(0, y), new Point(ActualWidth, y));
            dc.DrawLine(dashed, new Point(0, y), new Point(ActualWidth, y));
        }
        if(x >= 0 && x <= ActualWidth) {
            dc.DrawLine(light, new Point(x, 0), new Point(x, ActualHeight));
            dc.DrawLine(dashed, new Point(x, 0), new Point(x, ActualHeight));
        }
    }

    private static Brush freeze(Brush brush) {
        brush.Freeze();
        return brush;
    }
}
