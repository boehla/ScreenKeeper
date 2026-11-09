using System.Windows;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

/// <summary>
/// Feste Maße statt Schrift/Zeigerbild, damit Treffertests ohne WPF-Darstellung laufen.
/// </summary>
public sealed class FakeMeasure : IAnnotationMeasure {
    public Rect GetTextBounds(TextAnnotation annotation) {
        return new Rect(annotation.X, annotation.Y, 50, 20);
    }

    public Rect GetCursorBounds(CursorAnnotation annotation) {
        return new Rect(annotation.X - 1, annotation.Y - 1, 12, 19);
    }
}

public class AnnotationHitTestingTests {
    private static readonly FakeMeasure measure = new FakeMeasure();

    private static bool hit(Annotation annotation, double x, double y, double tolerance = 1) {
        return AnnotationHitTesting.HitTest(annotation, x, y, tolerance, measure);
    }

    [Fact]
    public void Rectangle_WithoutFill_HitsOnlyTheFrame() {
        RectangleAnnotation rectangle = new RectangleAnnotation { Rect = new PixelRect(10, 10, 100, 60), Thickness = 4 };
        Assert.True(hit(rectangle, 11, 40));
        Assert.True(hit(rectangle, 9.5, 40));
        Assert.True(hit(rectangle, 60, 69));
        Assert.False(hit(rectangle, 60, 40));
        Assert.False(hit(rectangle, 5, 40));
        rectangle.Fill = rectangle.Stroke;
        Assert.True(hit(rectangle, 60, 40));
    }

    [Fact]
    public void Ellipse_HitsOutlineOrFilledArea() {
        EllipseAnnotation ellipse = new EllipseAnnotation { Rect = new PixelRect(0, 0, 100, 50), Thickness = 2 };
        Assert.True(hit(ellipse, 50, 1));
        Assert.True(hit(ellipse, 99, 25));
        Assert.False(hit(ellipse, 50, 25));
        Assert.False(hit(ellipse, 2, 2));
        ellipse.Fill = ellipse.Stroke;
        Assert.True(hit(ellipse, 50, 25));
        Assert.False(hit(ellipse, 2, 2));
    }

    [Fact]
    public void Line_UsesDistanceToSegment() {
        LineAnnotation line = new LineAnnotation { Points = new List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(100, 100) }, Thickness = 2 };
        Assert.True(hit(line, 50, 50));
        Assert.True(hit(line, 51, 49));
        Assert.False(hit(line, 60, 40));
        Assert.False(hit(line, 120, 120));
    }

    [Fact]
    public void Arrow_HitsHeadArea() {
        ArrowAnnotation arrow = new ArrowAnnotation { Points = new List<PixelPoint> { new PixelPoint(0, 50), new PixelPoint(100, 50) }, Thickness = 4 };
        // seitlich in der Spitze, weit weg vom Schaft
        Assert.True(hit(arrow, 88, 55, 0));
        Assert.False(hit(arrow, 40, 58, 0));
    }

    [Fact]
    public void Freehand_HitsAnySegment() {
        FreehandAnnotation freehand = new FreehandAnnotation {
            Points = new List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(50, 0), new PixelPoint(50, 50) },
            Thickness = 2
        };
        Assert.True(hit(freehand, 25, 1));
        Assert.True(hit(freehand, 51, 25));
        Assert.False(hit(freehand, 25, 25));
    }

    [Fact]
    public void Areas_TextStampCursor_UseBounds() {
        Assert.True(hit(new HighlightAnnotation { Rect = new PixelRect(0, 0, 10, 10) }, 5, 5));
        Assert.True(hit(new PixelateAnnotation { Rect = new PixelRect(0, 0, 10, 10) }, 5, 5));
        Assert.True(hit(new BlurAnnotation { Rect = new PixelRect(0, 0, 10, 10) }, 5, 5));
        Assert.False(hit(new BlurAnnotation { Rect = new PixelRect(0, 0, 10, 10) }, 15, 5));
        Assert.True(hit(new TextAnnotation { X = 100, Y = 100 }, 140, 110));
        Assert.False(hit(new TextAnnotation { X = 100, Y = 100 }, 160, 110));
        Assert.True(hit(new NumberStampAnnotation { X = 50, Y = 50, Size = 32 }, 60, 60));
        Assert.False(hit(new NumberStampAnnotation { X = 50, Y = 50, Size = 32 }, 64, 64));
        Assert.True(hit(new CursorAnnotation { X = 10, Y = 10, Visible = false }, 15, 25));
        Assert.False(hit(new UnknownAnnotation(System.Text.Json.JsonDocument.Parse("{\"type\":\"zukunft\"}").RootElement), 0, 0));
    }

    [Fact]
    public void FindTopmost_PrefersLastDrawn() {
        HighlightAnnotation below = new HighlightAnnotation { Rect = new PixelRect(0, 0, 100, 100) };
        HighlightAnnotation above = new HighlightAnnotation { Rect = new PixelRect(50, 50, 100, 100) };
        List<Annotation> list = new List<Annotation> { below, above };
        Assert.Same(above, AnnotationHitTesting.FindTopmost(list, 60, 60, 0, measure));
        Assert.Same(below, AnnotationHitTesting.FindTopmost(list, 10, 10, 0, measure));
        Assert.Null(AnnotationHitTesting.FindTopmost(list, 200, 10, 0, measure));
    }

    [Fact]
    public void DistanceToOutline_InsideAndOutside() {
        Rect rect = new Rect(0, 0, 10, 10);
        Assert.Equal(2, AnnotationHitTesting.DistanceToOutline(rect, 2, 5));
        Assert.Equal(3, AnnotationHitTesting.DistanceToOutline(rect, 13, 5));
        Assert.Equal(5, AnnotationHitTesting.DistanceToOutline(rect, 13, 14));
    }

    [Fact]
    public void TextBounds_RealMeasure_GrowsWithFontSize() {
        Sta.Run(() => {
            AnnotationMeasure real = new AnnotationMeasure(new CaptureDocument());
            TextAnnotation small = new TextAnnotation { X = 10, Y = 20, Text = "Hallo", FontSize = 12 };
            TextAnnotation large = new TextAnnotation { X = 10, Y = 20, Text = "Hallo", FontSize = 48 };
            Rect smallBounds = real.GetTextBounds(small);
            Rect largeBounds = real.GetTextBounds(large);
            Assert.Equal(10, smallBounds.X);
            Assert.Equal(20, smallBounds.Y);
            Assert.True(largeBounds.Width > smallBounds.Width * 3);
            Assert.True(largeBounds.Height > smallBounds.Height * 3);
            // Umbruchbreite bestimmt die Breite
            TextAnnotation wrapped = new TextAnnotation { X = 0, Y = 0, Text = "ein langer Text mit vielen Wörtern", FontSize = 20, Width = 80 };
            Rect wrappedBounds = real.GetTextBounds(wrapped);
            Assert.Equal(80 + 2 * TextLayout.GetPadding(20), wrappedBounds.Width);
            Assert.True(wrappedBounds.Height > 2 * 20);
        });
    }
}
