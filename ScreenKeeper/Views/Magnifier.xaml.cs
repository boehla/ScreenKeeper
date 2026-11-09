using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Views;

/// <summary>
/// Lupe: liest direkt aus dem Original-Bitmap (nicht vom Bildschirm), vergrößert ohne Glättung, Pixelraster,
/// Fadenkreuz auf dem Mittelpixel; darunter Position, Bereichsgröße und Farbwert. Außerhalb des Bildes schwarz.
/// </summary>
public partial class Magnifier : UserControl {
    private BitmapSource? source;
    private PixelRect sourceBounds;
    private int zoom = 8;
    private int cells;
    private WriteableBitmap? bitmap;
    private byte[] buffer = Array.Empty<byte>();
    private byte[] rowBuffer = Array.Empty<byte>();
    private PixelPoint? lastFocus;
    private PixelRect? lastSelection;

    public Magnifier() {
        InitializeComponent();
    }

    /// <summary>
    /// Quelle und Größe festlegen. <paramref name="zoom"/> = Gerätepixel je Bildpixel, <paramref name="sizeDip"/> = gewünschte
    /// Kantenlänge, <paramref name="deviceScale"/> = Gerätepixel je DIP. Teuer genug, um es nur bei Änderungen aufzurufen.
    /// </summary>
    public void Configure(BitmapSource original, int zoom, double sizeDip, double deviceScale) {
        source = original.Format == PixelFormats.Bgra32 ? original : ImageFiles.ToBgra32(original);
        sourceBounds = new PixelRect(0, 0, source.PixelWidth, source.PixelHeight);
        this.zoom = Math.Max(1, zoom);
        cells = MagnifierPlacement.GetCellCount(sizeDip * deviceScale, this.zoom);
        bitmap = new WriteableBitmap(cells, cells, 96, 96, PixelFormats.Bgra32, null);
        buffer = new byte[cells * cells * 4];
        pixelImage.Source = bitmap;

        double cell = this.zoom / deviceScale;
        double size = cells * cell;
        double px = 1 / deviceScale;
        lens.Width = size;
        lens.Height = size;
        pixelImage.Width = size;
        pixelImage.Height = size;

        // Raster: 1 Gerätepixel breite Linien zwischen den vergrößerten Pixeln
        GeometryGroup grid = new GeometryGroup();
        for(int i = 1; i < cells; i++) {
            grid.Children.Add(new RectangleGeometry(new Rect(i * cell, 0, px, size)));
            grid.Children.Add(new RectangleGeometry(new Rect(0, i * cell, size, px)));
        }
        grid.Freeze();
        gridPath.Data = grid;

        // Fadenkreuz: halbtransparente Bänder durch Mittelzeile und -spalte, Mittelpixel frei
        int center = cells / 2;
        double c0 = center * cell;
        double c1 = c0 + cell;
        GeometryGroup cross = new GeometryGroup();
        cross.Children.Add(new RectangleGeometry(new Rect(c0, 0, cell, c0 - 2 * px)));
        cross.Children.Add(new RectangleGeometry(new Rect(c0, c1 + 2 * px, cell, Math.Max(0, size - c1 - 2 * px))));
        cross.Children.Add(new RectangleGeometry(new Rect(0, c0, c0 - 2 * px, cell)));
        cross.Children.Add(new RectangleGeometry(new Rect(c1 + 2 * px, c0, Math.Max(0, size - c1 - 2 * px), cell)));
        cross.Freeze();
        crossPath.Data = cross;

        // Mittelpixel: schwarzer Rahmen direkt außen, weißer Rahmen darum (gut sichtbar auf jedem Hintergrund)
        centerInnerPath.Data = frame(new Rect(c0 - px, c0 - px, cell + 2 * px, cell + 2 * px), px);
        centerOuterPath.Data = frame(new Rect(c0 - 2 * px, c0 - 2 * px, cell + 4 * px, cell + 4 * px), px);

        lastFocus = null;
        lastSelection = null;
    }

    /// <summary>
    /// Zeigt die Umgebung von <paramref name="focus"/>. Tut nichts, wenn sich nichts geändert hat.
    /// </summary>
    public void Show(PixelPoint focus, PixelRect? selection) {
        if(source == null || bitmap == null) return;
        if(focus == lastFocus && selection == lastSelection) return;
        bool pixelsChanged = focus != lastFocus;
        lastFocus = focus;
        lastSelection = selection;

        int half = cells / 2;
        if(pixelsChanged) {
            fillPixels(new PixelRect(focus.X - half, focus.Y - half, cells, cells));
            bitmap.WritePixels(new Int32Rect(0, 0, cells, cells), buffer, cells * 4, 0);
        }

        string color = "#------";
        if(sourceBounds.Contains(focus.X, focus.Y)) {
            int i = (half * cells + half) * 4;
            color = MagnifierPlacement.FormatColor(buffer[i + 2], buffer[i + 1], buffer[i]);
            SolidColorBrush swatch = new SolidColorBrush(Color.FromRgb(buffer[i + 2], buffer[i + 1], buffer[i]));
            swatch.Freeze();
            colorSwatch.Background = swatch;
        } else {
            colorSwatch.Background = Brushes.Black;
        }
        infoText.Text = MagnifierPlacement.FormatInfo(focus, selection, color);
    }

    private void fillPixels(PixelRect area) {
        // schwarz, deckend
        for(int i = 0; i < buffer.Length; i += 4) {
            buffer[i] = 0;
            buffer[i + 1] = 0;
            buffer[i + 2] = 0;
            buffer[i + 3] = 255;
        }
        PixelRect inside = area.Intersect(sourceBounds);
        if(inside.IsEmpty || source == null) return;
        int rowBytes = inside.Width * 4;
        int needed = rowBytes * inside.Height;
        if(rowBuffer.Length < needed) rowBuffer = new byte[needed];
        source.CopyPixels(new Int32Rect(inside.X, inside.Y, inside.Width, inside.Height), rowBuffer, rowBytes, 0);
        for(int row = 0; row < inside.Height; row++) {
            int target = ((inside.Y - area.Y + row) * cells + (inside.X - area.X)) * 4;
            Buffer.BlockCopy(rowBuffer, row * rowBytes, buffer, target, rowBytes);
        }
    }

    private static Geometry frame(Rect outer, double thickness) {
        Rect inner = new Rect(outer.Left + thickness, outer.Top + thickness, Math.Max(0, outer.Width - 2 * thickness), Math.Max(0, outer.Height - 2 * thickness));
        Geometry geometry = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(outer), new RectangleGeometry(inner));
        geometry.Freeze();
        return geometry;
    }
}
