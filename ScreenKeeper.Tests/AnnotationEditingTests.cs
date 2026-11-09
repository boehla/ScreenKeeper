using System.Windows;
using ScreenKeeper.Models;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

public class AnnotationEditingTests {
    private static readonly FakeMeasure measure = new FakeMeasure();

    [Fact]
    public void Moved_ShiftsEveryType_AndLeavesOriginal() {
        List<Annotation> annotations = new List<Annotation> {
            new RectangleAnnotation { Rect = new PixelRect(1, 2, 3, 4) },
            new EllipseAnnotation { Rect = new PixelRect(1, 2, 3, 4) },
            new HighlightAnnotation { Rect = new PixelRect(1, 2, 3, 4) },
            new PixelateAnnotation { Rect = new PixelRect(1, 2, 3, 4) },
            new BlurAnnotation { Rect = new PixelRect(1, 2, 3, 4) },
            new LineAnnotation { Points = new List<PixelPoint> { new PixelPoint(1, 2), new PixelPoint(3, 4) } },
            new ArrowAnnotation { Points = new List<PixelPoint> { new PixelPoint(1, 2), new PixelPoint(3, 4) } },
            new FreehandAnnotation { Points = new List<PixelPoint> { new PixelPoint(1, 2), new PixelPoint(3, 4) } },
            new TextAnnotation { X = 1, Y = 2 },
            new NumberStampAnnotation { X = 1, Y = 2 },
            new CursorAnnotation { X = 1, Y = 2 }
        };
        foreach(Annotation original in annotations) {
            Rect before = AnnotationEditing.GetBounds(original, measure);
            Annotation moved = AnnotationEditing.Moved(original, 10, -5);
            Rect after = AnnotationEditing.GetBounds(moved, measure);
            Assert.Equal(original.Id, moved.Id);
            Assert.Equal(before.X + 10, after.X, 6);
            Assert.Equal(before.Y - 5, after.Y, 6);
            Assert.Equal(before, AnnotationEditing.GetBounds(original, measure));
        }
    }

    [Fact]
    public void Resize_RectangleCorner_LikeSelectionHandles() {
        RectangleAnnotation rectangle = new RectangleAnnotation { Rect = new PixelRect(10, 10, 20, 10) };
        // rechts unten (Pixel 29/19) um 5/3 ziehen
        RectangleAnnotation resized = (RectangleAnnotation)AnnotationEditing.Resized(rectangle, ObjectHandle.BottomRight, new PixelPoint(30, 20), new PixelPoint(35, 23), false, measure);
        Assert.Equal(new PixelRect(10, 10, 25, 13), resized.Rect);
        // links über die rechte Kante hinaus → klappt um
        RectangleAnnotation flipped = (RectangleAnnotation)AnnotationEditing.Resized(rectangle, ObjectHandle.Left, new PixelPoint(10, 15), new PixelPoint(40, 15), false, measure);
        Assert.Equal(new PixelRect(29, 10, 12, 10), flipped.Rect);
        Assert.Equal(new PixelRect(10, 10, 20, 10), rectangle.Rect);
    }

    [Fact]
    public void Resize_LineEndpoint_WithShiftSnaps() {
        LineAnnotation line = new LineAnnotation { Points = new List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(10, 0) } };
        LineAnnotation moved = (LineAnnotation)AnnotationEditing.Resized(line, ObjectHandle.End, new PixelPoint(10, 0), new PixelPoint(20, 3), false, measure);
        Assert.Equal(new PixelPoint(20, 3), moved.Points[1]);
        Assert.Equal(new PixelPoint(0, 0), moved.Points[0]);
        LineAnnotation snapped = (LineAnnotation)AnnotationEditing.Resized(line, ObjectHandle.End, new PixelPoint(10, 0), new PixelPoint(20, 3), true, measure);
        Assert.Equal(new PixelPoint(20, 0), snapped.Points[1]);
        LineAnnotation start = (LineAnnotation)AnnotationEditing.Resized(line, ObjectHandle.Start, new PixelPoint(0, 0), new PixelPoint(-4, 6), false, measure);
        Assert.Equal(new PixelPoint(-4, 6), start.Points[0]);
    }

    [Fact]
    public void Resize_Freehand_ScalesPoints() {
        FreehandAnnotation freehand = new FreehandAnnotation {
            Points = new List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(10, 5), new PixelPoint(20, 10) }
        };
        FreehandAnnotation resized = (FreehandAnnotation)AnnotationEditing.Resized(freehand, ObjectHandle.BottomRight, new PixelPoint(21, 11), new PixelPoint(41, 21), false, measure);
        Assert.Equal(new[] { new PixelPoint(0, 0), new PixelPoint(20, 10), new PixelPoint(40, 20) }, resized.Points);
    }

    [Fact]
    public void Resize_Stamp_KeepsCenter() {
        NumberStampAnnotation stamp = new NumberStampAnnotation { X = 100, Y = 100, Size = 32 };
        // Ecke rechts unten (116/116) auf 126/120 ziehen → Abstand 26 → Durchmesser 52
        NumberStampAnnotation resized = (NumberStampAnnotation)AnnotationEditing.Resized(stamp, ObjectHandle.BottomRight, new PixelPoint(116, 116), new PixelPoint(126, 120), false, measure);
        Assert.Equal(52, resized.Size);
        Assert.Equal(100, resized.X);
        Assert.Equal(100, resized.Y);
        NumberStampAnnotation tiny = (NumberStampAnnotation)AnnotationEditing.Resized(stamp, ObjectHandle.TopLeft, new PixelPoint(84, 84), new PixelPoint(100, 100), false, measure);
        Assert.Equal(AnnotationEditing.MinStampSize, tiny.Size);
    }

    [Fact]
    public void Resize_Text_SidesSetWrapWidth_CornersScaleFont() {
        TextAnnotation text = new TextAnnotation { X = 10, Y = 10, FontSize = 20, Text = "abc" };
        // FakeMeasure: 50 × 20; rechte Kante (60) auf 90 ziehen
        TextAnnotation wider = (TextAnnotation)AnnotationEditing.Resized(text, ObjectHandle.Right, new PixelPoint(60, 15), new PixelPoint(90, 15), false, measure);
        Assert.Equal(80 - 2 * (int)ScreenKeeper.Services.TextLayout.GetPadding(20), wider.Width);
        Assert.Equal(20, wider.FontSize);
        // Ecke rechts unten: Höhe 20 → 40 verdoppelt die Schrift
        TextAnnotation larger = (TextAnnotation)AnnotationEditing.Resized(text, ObjectHandle.BottomRight, new PixelPoint(60, 30), new PixelPoint(80, 50), false, measure);
        Assert.Equal(40, larger.FontSize);
        Assert.Equal(10, larger.X);
        Assert.Equal(10, larger.Y);
    }

    [Fact]
    public void Handles_DependOnType() {
        Assert.Equal(8, AnnotationEditing.GetHandles(new RectangleAnnotation()).Count);
        Assert.Equal(8, AnnotationEditing.GetHandles(new FreehandAnnotation()).Count);
        Assert.Equal(new[] { ObjectHandle.Start, ObjectHandle.End }, AnnotationEditing.GetHandles(new ArrowAnnotation()));
        Assert.Equal(4, AnnotationEditing.GetHandles(new NumberStampAnnotation()).Count);
        Assert.Empty(AnnotationEditing.GetHandles(new CursorAnnotation()));

        RectangleAnnotation rectangle = new RectangleAnnotation { Rect = new PixelRect(10, 20, 30, 40) };
        Assert.Equal(new Point(40, 60), AnnotationEditing.GetHandlePoint(rectangle, ObjectHandle.BottomRight, measure));
        Assert.Equal(ObjectHandle.BottomRight, AnnotationEditing.HitHandle(rectangle, 41, 59, 3, measure));
        Assert.Equal(ObjectHandle.Top, AnnotationEditing.HitHandle(rectangle, 25, 21, 3, measure));
        Assert.Equal(ObjectHandle.None, AnnotationEditing.HitHandle(rectangle, 25, 40, 3, measure));
        ArrowAnnotation arrow = new ArrowAnnotation { Points = new List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(50, 50) } };
        Assert.Equal(ObjectHandle.End, AnnotationEditing.HitHandle(arrow, 50.5, 50.5, 3, measure));
    }
}
