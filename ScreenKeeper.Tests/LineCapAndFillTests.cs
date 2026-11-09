using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;
using Xunit.Abstractions;

namespace ScreenKeeper.Tests;

/// <summary>
/// Linienenden (Geometrie, Treffertest, Umriss, Pixel) und eigene Füllfarbe von Rechteck/Ellipse (Pixel).
/// </summary>
public class LineCapAndFillTests {
    private static readonly FakeMeasure measure = new FakeMeasure();
    private readonly ITestOutputHelper output;

    public LineCapAndFillTests(ITestOutputHelper output) {
        this.output = output;
    }

    private static List<PixelPoint> points(params int[] xy) {
        List<PixelPoint> result = new List<PixelPoint>();
        for(int i = 0; i + 1 < xy.Length; i += 2) result.Add(new PixelPoint(xy[i], xy[i + 1]));
        return result;
    }

    // ---------- Geometrie ----------

    [Fact]
    public void CapShapes_SitOnEndpoint_PointOutward_AndScaleWithThickness() {
        List<Point> line = new List<Point> { new Point(0, 0), new Point(100, 0) };
        (LineCapShape? start, LineCapShape? end) = AnnotationGeometry.GetLineCaps(line, 4, LineCaps.Dot, LineCaps.Arrow, false);
        Assert.NotNull(start);
        Assert.NotNull(end);
        Assert.Equal(new Point(0, 0), start!.Point);
        Assert.Equal(new Vector(-1, 0), start.Direction);
        Assert.Equal(AnnotationGeometry.GetDotCapRadius(4), start.Radius);
        // Pfeil wie bisher: Spitze am Endpunkt, Strich endet an der Grundlinie
        Assert.Equal(new Point(100, 0), end!.Polygon[0]);
        Assert.Equal(100 - AnnotationGeometry.GetArrowHeadLength(4), end.LineEnd.X, 6);
        Assert.True(end.TrimsLine);

        double radius = AnnotationGeometry.GetTriangleCapRadius(4);
        LineCapShape triangle = AnnotationGeometry.GetLineCapShape(LineCaps.Triangle, new Point(100, 0), new Vector(1, 0), 4, 100)!;
        // gleichseitig, Schwerpunkt auf dem Endpunkt, Spitze nach außen
        Assert.Equal(100 + radius, triangle.Polygon[0].X, 6);
        Point centroid = new Point(triangle.Polygon.Average(p => p.X), triangle.Polygon.Average(p => p.Y));
        Assert.Equal(100, centroid.X, 6);
        Assert.Equal(0, centroid.Y, 6);
        double side = (triangle.Polygon[0] - triangle.Polygon[1]).Length;
        Assert.Equal(side, (triangle.Polygon[1] - triangle.Polygon[2]).Length, 6);
        Assert.Equal(side, (triangle.Polygon[2] - triangle.Polygon[0]).Length, 6);
        Assert.False(triangle.TrimsLine);

        LineCapShape square = AnnotationGeometry.GetLineCapShape(LineCaps.Square, new Point(100, 0), new Vector(1, 0), 4, 100)!;
        double half = AnnotationGeometry.GetSquareCapHalfSize(4);
        Assert.Equal(100 - half, square.Bounds.Left, 6);
        Assert.Equal(100 + half, square.Bounds.Right, 6);
        Assert.Equal(-half, square.Bounds.Top, 6);
        Assert.Equal(half, square.Bounds.Bottom, 6);

        Assert.Null(AnnotationGeometry.GetLineCapShape(LineCaps.None, new Point(0, 0), new Vector(1, 0), 4, 100));
        Assert.Null(AnnotationGeometry.GetLineCapShape("stern", new Point(0, 0), new Vector(1, 0), 4, 100));
        Assert.True(AnnotationGeometry.GetDotCapRadius(12) > AnnotationGeometry.GetDotCapRadius(2));
        Assert.True(AnnotationGeometry.GetTriangleCapRadius(12) > AnnotationGeometry.GetTriangleCapRadius(2));
    }

    [Fact]
    public void EndDirection_FromLastPoints_OrSmoothTangent() {
        List<Point> diagonal = new List<Point> { new Point(0, 0), new Point(30, 40) };
        Vector end = AnnotationGeometry.GetEndDirection(diagonal, false, false);
        Assert.Equal(0.6, end.X, 6);
        Assert.Equal(0.8, end.Y, 6);
        Vector start = AnnotationGeometry.GetEndDirection(diagonal, true, false);
        Assert.Equal(-0.6, start.X, 6);
        Assert.Equal(-0.8, start.Y, 6);

        // Freihand: doppelte Endpunkte werden übersprungen
        List<Point> repeated = new List<Point> { new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(10, 10) };
        Assert.Equal(new Vector(0, 1), AnnotationGeometry.GetEndDirection(repeated, false, false));

        // geglättet: Tangente der Kurve am Ende = Richtung der Bézier-Steuerpunkte, die Kurve läuft dort genau so hinaus
        List<Point> curve = new List<Point> { new Point(0, 0), new Point(50, 0), new Point(100, 50), new Point(100, 100) };
        Vector tangent = AnnotationGeometry.GetEndDirection(curve, false, true);
        IReadOnlyList<ScreenKeeper.Services.BezierSegment> segments = AnnotationGeometry.SmoothPath(curve);
        Vector expected = segments[segments.Count - 1].End - segments[segments.Count - 1].Control2;
        expected.Normalize();
        Assert.Equal(expected.X, tangent.X, 6);
        Assert.Equal(expected.Y, tangent.Y, 6);
        // und stimmt mit der Richtung der letzten Stückchen der zerlegten Kurve überein
        List<Point> flat = AnnotationGeometry.FlattenSmoothPath(curve, 50);
        Vector last = flat[flat.Count - 1] - flat[flat.Count - 2];
        last.Normalize();
        Assert.True((last - tangent).Length < 0.02, $"Tangente {tangent}, Kurve {last}");
        Vector startTangent = AnnotationGeometry.GetEndDirection(curve, true, true);
        Assert.Equal(-1, startTangent.X, 6);
        Assert.Equal(0, startTangent.Y, 6);
    }

    [Fact]
    public void CappedStroke_ArrowTrimsToBase_OtherCapsKeepLine() {
        List<Point> line = new List<Point> { new Point(0, 0), new Point(100, 0) };
        CappedStroke plain = AnnotationGeometry.GetCappedStroke(line, 4, LineCaps.Dot, LineCaps.Square, false);
        Assert.Null(plain.TrimmedPath);

        // wie der bisherige Pfeil: Schaft bis zur Grundlinie plus höchstens 1 px in die Spitze
        CappedStroke arrow = AnnotationGeometry.GetCappedStroke(line, 4, LineCaps.None, LineCaps.Arrow, false);
        Assert.NotNull(arrow.TrimmedPath);
        Assert.Equal(new Point(0, 0), arrow.TrimmedPath![0]);
        Assert.Equal(100 - AnnotationGeometry.GetArrowHeadLength(4) + 1, arrow.TrimmedPath[1].X, 6);

        CappedStroke both = AnnotationGeometry.GetCappedStroke(line, 4, LineCaps.Arrow, LineCaps.Arrow, false);
        Assert.Equal(AnnotationGeometry.GetArrowHeadLength(4) - 1, both.TrimmedPath![0].X, 6);

        // sehr kurz: beide Spitzen teilen sich die Länge, kein Strich bleibt übrig
        CappedStroke tiny = AnnotationGeometry.GetCappedStroke(new List<Point> { new Point(0, 0), new Point(10, 0) }, 4, LineCaps.Arrow, LineCaps.Arrow, false);
        Assert.Empty(tiny.TrimmedPath!);
        Assert.True((tiny.Start!.LineEnd - tiny.Start.Point).Length < AnnotationGeometry.GetArrowHeadLength(4));

        // geglättete Freihand-Linie: endet kurz vor der Spitze (Abstand = Spitzenlänge − Überlappung)
        List<Point> curve = new List<Point> { new Point(0, 0), new Point(60, 10), new Point(120, 60), new Point(150, 140) };
        CappedStroke smooth = AnnotationGeometry.GetCappedStroke(curve, 4, LineCaps.None, LineCaps.Arrow, true);
        Point tip = curve[curve.Count - 1];
        double distance = (smooth.TrimmedPath![smooth.TrimmedPath.Count - 1] - tip).Length;
        Assert.InRange(distance, AnnotationGeometry.GetArrowHeadLength(4) - 1.5, AnnotationGeometry.GetArrowHeadLength(4) - 0.5);
        Assert.Equal(curve[0], smooth.TrimmedPath[0]);
        // die Grundlinie der Spitze sitzt auf dem Strich (keine Lücke, auch wenn sich die Kurve innerhalb der Spitze biegt)
        Assert.True((smooth.TrimmedPath[smooth.TrimmedPath.Count - 1] - smooth.End!.LineEnd).Length <= 1 + 1e-6);

        // starke Biegung am Ende: Tangente waagerecht, die Spitze folgt der Sehne und schließt an den Strich an
        List<Point> hook = new List<Point> { new Point(0, 0), new Point(40, 0), new Point(60, 40), new Point(70, 50), new Point(80, 50) };
        CappedStroke bent = AnnotationGeometry.GetCappedStroke(hook, 4, LineCaps.None, LineCaps.Arrow, true);
        Assert.True((bent.TrimmedPath![bent.TrimmedPath.Count - 1] - bent.End!.LineEnd).Length <= 1 + 1e-6);
        Assert.True(bent.End.Direction.Y > 0.2, $"Richtung {bent.End.Direction}");
        // Punkt, Quadrat und Dreieck folgen weiter der Tangente am Endpunkt
        LineCapShape triangle = AnnotationGeometry.GetLineCaps(hook, 4, LineCaps.None, LineCaps.Triangle, true).End!;
        Vector tangent = AnnotationGeometry.GetEndDirection(hook, false, true);
        Assert.Equal(tangent.X, triangle.Direction.X, 6);
        Assert.Equal(tangent.Y, triangle.Direction.Y, 6);
    }

    [Fact]
    public void TrimPolylineEnd_CutsWhereThePathLeavesTheCircle() {
        List<Point> path = new List<Point> { new Point(0, 0), new Point(100, 0), new Point(100, 10) };
        List<Point> trimmed = AnnotationGeometry.TrimPolylineEnd(path, 20);
        Point cut = trimmed[trimmed.Count - 1];
        Assert.Equal(20, (cut - path[2]).Length, 6);
        Assert.Equal(0, cut.Y, 6);
        Assert.Empty(AnnotationGeometry.TrimPolylineEnd(path, 1000));
    }

    // ---------- Treffertest und Umriss ----------

    [Fact]
    public void HitTest_IncludesCapSymbols() {
        LineAnnotation line = new LineAnnotation { Points = points(10, 50, 110, 50), Thickness = 2 };
        double radius = AnnotationGeometry.GetDotCapRadius(2);
        // hinter dem Ende, aber im Punkt-Symbol
        Assert.False(AnnotationHitTesting.HitTest(line, 110, 50 + radius - 0.5, 0, measure));
        line.EndCap = LineCaps.Dot;
        Assert.True(AnnotationHitTesting.HitTest(line, 110, 50 + radius - 0.5, 0, measure));
        Assert.False(AnnotationHitTesting.HitTest(line, 110, 50 + radius + 2, 0, measure));
        Assert.True(AnnotationHitTesting.HitTest(line, 110, 50 + radius + 2, 2.5, measure));

        // Dreieck am Anfang ragt nach außen
        line.StartCap = LineCaps.Triangle;
        double apex = AnnotationGeometry.GetTriangleCapRadius(2);
        Assert.True(AnnotationHitTesting.HitTest(line, 10 - apex + 1, 50, 0, measure));
        Assert.False(AnnotationHitTesting.HitTest(line, 10 - apex - 2, 50, 0, measure));

        // Pfeil ohne Spitze: keine Fläche mehr seitlich am Ende
        ArrowAnnotation arrow = new ArrowAnnotation { Points = points(0, 50, 100, 50), Thickness = 4 };
        Assert.True(AnnotationHitTesting.HitTest(arrow, 88, 55, 0, measure));
        arrow.EndCap = LineCaps.None;
        Assert.False(AnnotationHitTesting.HitTest(arrow, 88, 55, 0, measure));
        arrow.StartCap = LineCaps.Arrow;
        Assert.True(AnnotationHitTesting.HitTest(arrow, 12, 55, 0, measure));

        // Freihand mit Quadrat am Ende
        FreehandAnnotation freehand = new FreehandAnnotation { Points = points(0, 0, 50, 0, 50, 50), Thickness = 2, EndCap = LineCaps.Square };
        double half = AnnotationGeometry.GetSquareCapHalfSize(2);
        Assert.True(AnnotationHitTesting.HitTest(freehand, 50 + half - 0.5, 50 + half - 0.5, 0, measure));
    }

    [Fact]
    public void Bounds_GrowByCapSymbols() {
        LineAnnotation line = new LineAnnotation { Points = points(10, 50, 110, 50), Thickness = 4 };
        Rect plain = AnnotationEditing.GetBounds(line, measure);
        Assert.Equal(new Rect(10, 50, 101, 1), plain);
        line.StartCap = LineCaps.Dot;
        line.EndCap = LineCaps.Triangle;
        Rect capped = AnnotationEditing.GetBounds(line, measure);
        double radius = AnnotationGeometry.GetDotCapRadius(4);
        Assert.True(capped.Left <= 10 - radius + 1e-6);
        Assert.True(capped.Right >= 110 + AnnotationGeometry.GetTriangleCapRadius(4));
        Assert.True(capped.Height >= 2 * radius);

        FreehandAnnotation freehand = new FreehandAnnotation { Points = points(0, 0, 50, 0, 50, 50), EndCap = LineCaps.Dot };
        Assert.True(AnnotationEditing.GetBounds(freehand, measure).Bottom > 51);
    }

    // ---------- Pixel ----------

    private static BitmapSource whiteImage(int width, int height) {
        byte[] pixels = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Farbe des Pixels (x, y) als (R, G, B).</summary>
    private static (int R, int G, int B) pixel(byte[] pixels, int width, int x, int y) {
        int i = (y * width + x) * 4;
        return (pixels[i + 2], pixels[i + 1], pixels[i]);
    }

    private static byte[] render(BitmapSource original, params Annotation[] annotations) {
        CaptureDocument document = new CaptureDocument { Annotations = annotations.ToList() };
        return TestImages.GetPixels(new RenderService().Render(document, original, new PixelRect(0, 0, original.PixelWidth, original.PixelHeight)));
    }

    [Fact]
    public void Export_CapSymbols_AreDrawnInStrokeColor() {
        Sta.Run(() => {
            if(!RenderCheck.IsRenderTargetBitmapWorking()) {
                output.WriteLine("Übersprungen: RenderTargetBitmap liefert ohne aktive Sitzung leere Pixel.");
                return;
            }
            const int width = 160;
            BitmapSource original = whiteImage(width, 120);
            (int, int, int) blue = (0x1E, 0x88, 0xE5);
            (int, int, int) white = (255, 255, 255);
            LineAnnotation line = new LineAnnotation { Points = points(20, 30, 100, 30), Thickness = 2, Stroke = "#FF1E88E5", StartCap = LineCaps.Dot, EndCap = LineCaps.Square };
            byte[] pixels = render(original, line);
            // Punkt am Anfang: neben dem Strich, innerhalb des Radius (1,5 × 2 + 2 = 5)
            Assert.Equal(blue, pixel(pixels, width, 20, 33));
            Assert.Equal(blue, pixel(pixels, width, 18, 27));
            // Quadrat am Ende (halbe Kante 4,6): Ecke und hinter dem Endpunkt
            Assert.Equal(blue, pixel(pixels, width, 103, 33));
            Assert.Equal(blue, pixel(pixels, width, 97, 26));
            Assert.Equal(white, pixel(pixels, width, 107, 30));
            Assert.Equal(white, pixel(pixels, width, 60, 33));

            // ohne Endsymbole an denselben Stellen nur Hintergrund
            line.StartCap = LineCaps.None;
            line.EndCap = LineCaps.None;
            pixels = render(original, line);
            Assert.Equal(white, pixel(pixels, width, 20, 33));
            Assert.Equal(white, pixel(pixels, width, 103, 33));

            // Pfeil mit Dreieck am Anfang (Umkreisradius 2 × 4 + 3,5 = 11,5): Spitze ragt 11 px vor den Anfang
            ArrowAnnotation arrow = new ArrowAnnotation { Points = points(30, 80, 130, 80), Thickness = 4, Stroke = "#FFE53935", StartCap = LineCaps.Triangle };
            pixels = render(original, arrow);
            (int, int, int) red = (0xE5, 0x39, 0x35);
            Assert.Equal(red, pixel(pixels, width, 21, 80));
            Assert.Equal(red, pixel(pixels, width, 32, 86));
            Assert.Equal(white, pixel(pixels, width, 16, 80));
            // Spitze am Ende wie bisher, nichts steht über die Spitze hinaus
            Assert.Equal(red, pixel(pixels, width, 115, 84));
            Assert.Equal(white, pixel(pixels, width, 131, 80));
        });
    }

    [Fact]
    public void Export_FreehandArrow_EndsInHead_WithoutOverhang() {
        Sta.Run(() => {
            if(!RenderCheck.IsRenderTargetBitmapWorking()) {
                output.WriteLine("Übersprungen: RenderTargetBitmap liefert ohne aktive Sitzung leere Pixel.");
                return;
            }
            const int width = 160;
            BitmapSource original = whiteImage(width, 120);
            FreehandAnnotation freehand = new FreehandAnnotation { Points = points(10, 100, 60, 90, 100, 60, 120, 20), Thickness = 4, Stroke = "#FF43A047", EndCap = LineCaps.Arrow, StartCap = LineCaps.Dot };
            byte[] pixels = render(original, freehand);
            (int, int, int) green = (0x43, 0xA0, 0x47);
            // Spitze zeigt in Richtung der letzten Strecke (nach oben rechts): Pixel kurz vor der Spitze ist grün
            Vector direction = new Vector(20, -40);
            direction.Normalize();
            Point inside = new Point(120, 20) - direction * 6;
            Assert.Equal(green, pixel(pixels, width, (int)inside.X, (int)inside.Y));
            // hinter der Spitze nichts
            Point beyond = new Point(120, 20) + direction * 3;
            Assert.Equal((255, 255, 255), pixel(pixels, width, (int)beyond.X, (int)beyond.Y));
            // Punkt am Anfang (Radius 8)
            Assert.Equal(green, pixel(pixels, width, 10, 106));
        });
    }

    [Fact]
    public void Export_FillColor_InsideAndStrokeOutside() {
        Sta.Run(() => {
            if(!RenderCheck.IsRenderTargetBitmapWorking()) {
                output.WriteLine("Übersprungen: RenderTargetBitmap liefert ohne aktive Sitzung leere Pixel.");
                return;
            }
            const int width = 160;
            BitmapSource original = whiteImage(width, 120);
            RectangleAnnotation rectangle = new RectangleAnnotation { Rect = new PixelRect(10, 10, 60, 40), Thickness = 4, Stroke = "#FF1E88E5", Fill = "#FF43A047" };
            EllipseAnnotation ellipse = new EllipseAnnotation { Rect = new PixelRect(80, 10, 70, 50), Thickness = 4, Stroke = "#FF000000", Fill = "#80FF0000" };
            byte[] pixels = render(original, rectangle, ellipse);
            // Rechteck: innen Füllfarbe, am Rand Rahmenfarbe, außen Hintergrund
            Assert.Equal((0x43, 0xA0, 0x47), pixel(pixels, width, 40, 30));
            Assert.Equal((0x1E, 0x88, 0xE5), pixel(pixels, width, 11, 30));
            Assert.Equal((0x1E, 0x88, 0xE5), pixel(pixels, width, 40, 48));
            Assert.Equal((255, 255, 255), pixel(pixels, width, 5, 30));
            // Ellipse: halbtransparentes Rot über Weiß, schwarzer Rand
            (int r, int g, int b) center = pixel(pixels, width, 115, 35);
            Assert.Equal(255, center.r);
            Assert.InRange(center.g, 115, 140);
            Assert.InRange(center.b, 115, 140);
            Assert.Equal((0, 0, 0), pixel(pixels, width, 81, 35));
            Assert.Equal((255, 255, 255), pixel(pixels, width, 82, 12));
        });
    }
}
