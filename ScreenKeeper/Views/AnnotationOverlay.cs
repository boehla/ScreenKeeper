using System.Windows;
using System.Windows.Media;
using ScreenKeeper.Models;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Tools;

namespace ScreenKeeper.Views;

/// <summary>
/// Zeichnet in Ansichts-DIPs (nicht gezoomt) die Hilfslinien für Zeichenobjekte: Rahmen und Anfasser des ausgewählten
/// Objekts (konstante Gerätepixel-Größe wie beim Bereich), den Rahmen eines gerade aufgezogenen Textfelds und
/// gestrichelte Umrisse ausgeblendeter Zeiger (damit man sie auswählen und wieder einblenden kann). Nicht im Export.
/// </summary>
public sealed class AnnotationOverlay : FrameworkElement {
    private static readonly Brush hiddenBrush = freeze(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)));

    private ViewTransform? transform;
    private double deviceScale = 1;
    private ObjectSelectionVisual? selection;
    private IReadOnlyList<Rect> hiddenCursors = Array.Empty<Rect>();
    private PixelRect? preview;

    public AnnotationOverlay() {
        IsHitTestVisible = false;
        Focusable = false;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    /// <param name="transform">Bild → Ansicht</param>
    /// <param name="deviceScale">Gerätepixel je DIP</param>
    /// <param name="selection">ausgewähltes Objekt (Bildpixel) oder null</param>
    /// <param name="hiddenCursors">Umrisse ausgeblendeter Zeiger (Bildpixel)</param>
    /// <param name="preview">gerade aufgezogenes Textfeld (Bildpixel) oder null</param>
    public void Update(ViewTransform transform, double deviceScale, ObjectSelectionVisual? selection, IReadOnlyList<Rect> hiddenCursors, PixelRect? preview) {
        this.transform = transform;
        this.deviceScale = deviceScale;
        this.selection = selection;
        this.hiddenCursors = hiddenCursors;
        this.preview = preview;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc) {
        if(transform == null) return;
        double px = 1 / deviceScale;
        Pen white = new Pen(Brushes.White, px);
        Pen dashed = new Pen(Brushes.Black, px) { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) };

        foreach(Rect hidden in hiddenCursors) {
            Rect frame = snap(toView(hidden), px);
            Pen hiddenPen = new Pen(hiddenBrush, px) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) };
            dc.DrawRectangle(null, hiddenPen, frame);
            dc.DrawLine(hiddenPen, frame.TopLeft, frame.BottomRight);
        }

        if(preview.HasValue) {
            Rect frame = snap(transform.ToView(preview.Value), px);
            dc.DrawRectangle(null, white, frame);
            dc.DrawRectangle(null, dashed, frame);
        }

        if(selection == null) return;
        if(selection.ShowFrame) {
            Rect frame = snap(toView(selection.Bounds), px);
            dc.DrawRectangle(null, white, frame);
            dc.DrawRectangle(null, dashed, frame);
        }
        Pen border = new Pen(Brushes.Black, px);
        double size = SelectionTool.HandleSizeDevice;
        foreach(Point handle in selection.Handles) {
            Point center = transform.ToView(handle.X, handle.Y);
            double left = Math.Round(center.X * deviceScale - size / 2) / deviceScale;
            double top = Math.Round(center.Y * deviceScale - size / 2) / deviceScale;
            Rect box = new Rect(left + px / 2, top + px / 2, (size - 1) * px, (size - 1) * px);
            if(selection.ShowFrame) {
                dc.DrawRectangle(Brushes.White, border, box);
            } else {
                // Endpunkte von Linie und Pfeil rund
                dc.DrawEllipse(Brushes.White, border, new Point(box.X + box.Width / 2, box.Y + box.Height / 2), box.Width / 2, box.Height / 2);
            }
        }
    }

    private Rect toView(Rect image) {
        Point topLeft = transform!.ToView(image.X, image.Y);
        return new Rect(topLeft.X, topLeft.Y, image.Width * transform.Zoom, image.Height * transform.Zoom);
    }

    /// <summary>
    /// Rahmen um eine Fläche, genau einen Gerätepixel außerhalb, auf Gerätepixel ausgerichtet.
    /// </summary>
    private Rect snap(Rect view, double px) {
        double left = Math.Floor(view.Left * deviceScale) / deviceScale;
        double top = Math.Floor(view.Top * deviceScale) / deviceScale;
        double right = Math.Ceiling(view.Right * deviceScale) / deviceScale;
        double bottom = Math.Ceiling(view.Bottom * deviceScale) / deviceScale;
        return new Rect(left - px / 2, top - px / 2, Math.Max(0, right - left) + px, Math.Max(0, bottom - top) + px);
    }

    private static Brush freeze(Brush brush) {
        brush.Freeze();
        return brush;
    }
}
