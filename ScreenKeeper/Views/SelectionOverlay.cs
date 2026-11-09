using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ScreenKeeper.Models;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Tools;

namespace ScreenKeeper.Views;

/// <summary>
/// Zeichnet in Ansichts-DIPs (nicht gezoomt): Abdunkelung außerhalb des Bereichs, 1 Gerätepixel breiten Rahmen
/// (weiß/schwarz gestrichelt, außen um den Bereich), 8 Anfasser in konstanter Gerätepixel-Größe und „B × H“.
/// Wird nur neu gezeichnet, wenn sich etwas geändert hat.
/// </summary>
public sealed class SelectionOverlay : FrameworkElement {
    private static readonly Brush dimBrush = freeze(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)));
    private static readonly Brush labelBackground = freeze(new SolidColorBrush(Color.FromArgb(0xD0, 0x20, 0x20, 0x20)));
    private static readonly Typeface labelFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private Rect imageArea;
    private Rect? selection;
    private PixelRect? selectionPixels;
    private double deviceScale = 1;
    private bool showHandles;

    public SelectionOverlay() {
        IsHitTestVisible = false;
        Focusable = false;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    /// <param name="imageArea">das ganze Bild in Ansichts-DIPs (nur dort wird abgedunkelt)</param>
    /// <param name="selection">Bereich in Ansichts-DIPs; null = kein Bereich, keine Abdunkelung</param>
    /// <param name="selectionPixels">Bereich in Bildpixeln (für die Größenanzeige)</param>
    /// <param name="deviceScale">Gerätepixel je DIP</param>
    public void Update(Rect imageArea, Rect? selection, PixelRect? selectionPixels, double deviceScale, bool showHandles) {
        if(imageArea == this.imageArea && selection == this.selection && selectionPixels == this.selectionPixels && deviceScale == this.deviceScale && showHandles == this.showHandles) return;
        this.imageArea = imageArea;
        this.selection = selection;
        this.selectionPixels = selectionPixels;
        this.deviceScale = deviceScale;
        this.showHandles = showHandles;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc) {
        if(!selection.HasValue || !selectionPixels.HasValue) return;
        Rect area = selection.Value;
        double px = 1 / deviceScale;

        // Abdunkelung als vier Rechtecke um den Bereich, begrenzt auf das Bild
        drawDim(dc, new Rect(new Point(imageArea.Left, imageArea.Top), new Point(imageArea.Right, area.Top)));
        drawDim(dc, new Rect(new Point(imageArea.Left, area.Bottom), new Point(imageArea.Right, imageArea.Bottom)));
        drawDim(dc, new Rect(new Point(imageArea.Left, area.Top), new Point(area.Left, area.Bottom)));
        drawDim(dc, new Rect(new Point(area.Right, area.Top), new Point(imageArea.Right, area.Bottom)));

        // Rahmen genau einen Gerätepixel außerhalb des Bereichs (verdeckt keine gewählten Pixel)
        Rect frame = new Rect(area.Left - px / 2, area.Top - px / 2, area.Width + px, area.Height + px);
        Pen white = new Pen(Brushes.White, px);
        Pen dashed = new Pen(Brushes.Black, px) { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) };
        dc.DrawRectangle(null, white, frame);
        dc.DrawRectangle(null, dashed, frame);

        if(showHandles) drawHandles(dc, area, px);
        drawSizeLabel(dc, area, selectionPixels.Value, px);
    }

    private void drawDim(DrawingContext dc, Rect rect) {
        rect.Intersect(imageArea);
        if(rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return;
        dc.DrawRectangle(dimBrush, null, rect);
    }

    private void drawHandles(DrawingContext dc, Rect area, double px) {
        Pen border = new Pen(Brushes.Black, px);
        double size = SelectionTool.HandleSizeDevice;
        foreach(SelectionHandle handle in SelectionGeometry.Handles) {
            Point center = getHandleCenter(area, handle);
            // in Gerätepixeln ausrichten, damit die Anfasser scharf sind
            double left = Math.Round(center.X * deviceScale - size / 2) / deviceScale;
            double top = Math.Round(center.Y * deviceScale - size / 2) / deviceScale;
            dc.DrawRectangle(Brushes.White, border, new Rect(left + px / 2, top + px / 2, (size - 1) * px, (size - 1) * px));
        }
    }

    private static Point getHandleCenter(Rect area, SelectionHandle handle) {
        double centerX = area.Left + area.Width / 2;
        double centerY = area.Top + area.Height / 2;
        switch(handle) {
            case SelectionHandle.TopLeft: return new Point(area.Left, area.Top);
            case SelectionHandle.Top: return new Point(centerX, area.Top);
            case SelectionHandle.TopRight: return new Point(area.Right, area.Top);
            case SelectionHandle.Right: return new Point(area.Right, centerY);
            case SelectionHandle.BottomRight: return new Point(area.Right, area.Bottom);
            case SelectionHandle.Bottom: return new Point(centerX, area.Bottom);
            case SelectionHandle.BottomLeft: return new Point(area.Left, area.Bottom);
            case SelectionHandle.Left: return new Point(area.Left, centerY);
        }
        return new Point(centerX, centerY);
    }

    private void drawSizeLabel(DrawingContext dc, Rect area, PixelRect pixels, double px) {
        string text = string.Create(CultureInfo.InvariantCulture, $"{pixels.Width} × {pixels.Height}");
        FormattedText formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, labelFace, 12, Brushes.White, deviceScale);
        double padX = 6;
        double padY = 2;
        double width = formatted.Width + 2 * padX;
        double height = formatted.Height + 2 * padY;
        double gap = SelectionTool.HandleSizeDevice * px;
        double x = area.Left;
        double y = area.Top - gap - height;
        // kein Platz über dem Bereich → innen links oben
        if(y < 0) {
            x = area.Left + gap;
            y = area.Top + gap;
        }
        x = Math.Max(0, Math.Min(x, ActualWidth - width));
        Rect box = new Rect(Math.Round(x * deviceScale) / deviceScale, Math.Round(y * deviceScale) / deviceScale, width, height);
        dc.DrawRoundedRectangle(labelBackground, null, box, 3, 3);
        dc.DrawText(formatted, new Point(box.Left + padX, box.Top + padY));
    }

    private static Brush freeze(Brush brush) {
        brush.Freeze();
        return brush;
    }
}
