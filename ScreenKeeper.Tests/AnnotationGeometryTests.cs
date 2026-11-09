using System.Windows;
using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.Tests;

public class AnnotationGeometryTests {
    [Theory]
    [InlineData(37, 4, 37, 0)]
    [InlineData(-37, 6, -37, 0)]
    [InlineData(3, 40, 0, 40)]
    [InlineData(-5, -40, 0, -40)]
    [InlineData(30, 34, 32, 32)]
    [InlineData(-30, 34, -32, 32)]
    [InlineData(-30, -26, -28, -28)]
    [InlineData(31, -30, 31, -31)]
    public void SnapAngle_Uses45DegreeRaster(int dx, int dy, int expectedDx, int expectedDy) {
        PixelPoint start = new PixelPoint(100, 200);
        PixelPoint snapped = AnnotationGeometry.SnapAngle(start, new PixelPoint(100 + dx, 200 + dy));
        Assert.Equal(new PixelPoint(100 + expectedDx, 200 + expectedDy), snapped);
    }

    [Fact]
    public void SnapAngle_SamePoint_IsUnchanged() {
        Assert.Equal(new PixelPoint(5, 5), AnnotationGeometry.SnapAngle(new PixelPoint(5, 5), new PixelPoint(5, 5)));
    }

    [Fact]
    public void ArrowHead_IsAtTipAndProportionalToThickness() {
        Point start = new Point(0, 0);
        Point tip = new Point(100, 0);
        ArrowHead thin = AnnotationGeometry.GetArrowHead(start, tip, 2);
        ArrowHead thick = AnnotationGeometry.GetArrowHead(start, tip, 8);

        Assert.Equal(tip, thin.Tip);
        Assert.Equal(100 - AnnotationGeometry.GetArrowHeadLength(2), thin.Base.X, 6);
        Assert.Equal(0, thin.Base.Y, 6);
        // Grundlinie senkrecht zur Richtung, symmetrisch
        Assert.Equal(thin.Base.X, thin.Left.X, 6);
        Assert.Equal(thin.Base.X, thin.Right.X, 6);
        Assert.Equal(-thin.Left.Y, thin.Right.Y, 6);
        Assert.Equal(AnnotationGeometry.GetArrowHeadHalfWidth(2), Math.Abs(thin.Left.Y), 6);
        // dickere Linie → größere Spitze
        Assert.True((thick.Tip - thick.Base).Length > (thin.Tip - thin.Base).Length);
        Assert.True(Math.Abs(thick.Left.Y) > Math.Abs(thin.Left.Y));
    }

    [Fact]
    public void ArrowHead_ShortArrow_ShrinksHead() {
        ArrowHead head = AnnotationGeometry.GetArrowHead(new Point(0, 0), new Point(0, 10), 4);
        Assert.True((head.Tip - head.Base).Length <= 10 + 1e-9);
        Assert.Equal(new Point(0, 10), head.Tip);
    }

    [Fact]
    public void SmoothPath_HasOneSegmentPerGapAndPassesThroughPoints() {
        List<Point> points = new List<Point> { new Point(0, 0), new Point(10, 5), new Point(20, 0), new Point(30, 8), new Point(40, 2) };
        IReadOnlyList<BezierSegment> segments = AnnotationGeometry.SmoothPath(points);
        Assert.Equal(points.Count - 1, segments.Count);
        for(int i = 0; i < segments.Count; i++) {
            Assert.Equal(points[i + 1], segments[i].End);
            Assert.False(double.IsNaN(segments[i].Control1.X) || double.IsNaN(segments[i].Control2.Y));
        }
        Assert.Empty(AnnotationGeometry.SmoothPath(new List<Point> { new Point(1, 1) }));
        Assert.Single(AnnotationGeometry.SmoothPath(new List<Point> { new Point(1, 1), new Point(5, 5) }));
    }

    [Fact]
    public void Simplify_RemovesCollinearAndDuplicatePoints() {
        List<PixelPoint> points = new List<PixelPoint>();
        for(int x = 0; x <= 20; x++) points.Add(new PixelPoint(x, 0));
        points.Add(new PixelPoint(20, 0));
        for(int y = 1; y <= 20; y++) points.Add(new PixelPoint(20, y));
        List<PixelPoint> result = AnnotationGeometry.Simplify(points, 0.75);
        Assert.Equal(new[] { new PixelPoint(0, 0), new PixelPoint(20, 0), new PixelPoint(20, 20) }, result);
        Assert.Equal(2, AnnotationGeometry.Simplify(new[] { new PixelPoint(1, 1), new PixelPoint(1, 1), new PixelPoint(4, 4) }, 0.75).Count);
    }

    [Fact]
    public void DistanceToSegment_IsPerpendicularOrToEndpoint() {
        Point a = new Point(0, 0);
        Point b = new Point(10, 0);
        Assert.Equal(3, AnnotationGeometry.DistanceToSegment(5, 3, a, b), 9);
        Assert.Equal(5, AnnotationGeometry.DistanceToSegment(13, 4, a, b), 9);
        Assert.Equal(2, AnnotationGeometry.DistanceToSegment(-2, 0, a, a), 9);
    }

    [Fact]
    public void StrokeRect_KeepsStrokeInsideAndOnPixelEdges() {
        Rect rect = AnnotationGeometry.GetStrokeRect(new PixelRect(10, 20, 30, 40), 4);
        Assert.Equal(new Rect(12, 22, 26, 36), rect);
        Assert.Equal(new Rect(10.5, 20.5, 29, 39), AnnotationGeometry.GetStrokeRect(new PixelRect(10, 20, 30, 40), 1));
        // dicker als das Rechteck → Strich in der Mitte
        Assert.Equal(new Rect(11, 21, 0, 0), AnnotationGeometry.GetStrokeRect(new PixelRect(10, 20, 2, 2), 8));
    }

    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(2, 0)]
    [InlineData(4, 0)]
    [InlineData(6, 0)]
    [InlineData(3, 0.5)]
    public void StrokeOffset_DependsOnParity(double thickness, double expected) {
        Assert.Equal(expected, AnnotationGeometry.GetStrokeOffset(thickness));
    }

    [Fact]
    public void ScalePoints_MapsCornersToCorners() {
        List<PixelPoint> points = new List<PixelPoint> { new PixelPoint(10, 10), new PixelPoint(20, 15), new PixelPoint(30, 20) };
        PixelRect from = AnnotationGeometry.GetBounds(points);
        Assert.Equal(new PixelRect(10, 10, 21, 11), from);
        PixelRect to = new PixelRect(100, 50, 41, 21);
        List<PixelPoint> scaled = AnnotationGeometry.ScalePoints(points, from, to);
        Assert.Equal(new[] { new PixelPoint(100, 50), new PixelPoint(120, 60), new PixelPoint(140, 70) }, scaled);
        Assert.Equal(to, AnnotationGeometry.GetBounds(scaled));
    }

    [Fact]
    public void TriangleTest_IncludesInsideAndEdges() {
        Point a = new Point(0, 0);
        Point b = new Point(10, 0);
        Point c = new Point(0, 10);
        Assert.True(AnnotationGeometry.IsInTriangle(2, 2, a, b, c));
        Assert.True(AnnotationGeometry.IsInTriangle(5, 0, a, b, c));
        Assert.False(AnnotationGeometry.IsInTriangle(8, 8, a, b, c));
    }
}
