using System.Windows;
using ScreenKeeper.Models;

namespace ScreenKeeper.Services;

/// <summary>
/// Maße, die nur mit WPF-Mitteln bestimmbar sind (Textgröße, Zeigerbild) – für Treffertests und Anfasser.
/// </summary>
public interface IAnnotationMeasure {
    /// <summary>Textfeld in Bildpixeln.</summary>
    Rect GetTextBounds(TextAnnotation annotation);

    /// <summary>Zeigerbild in Bildpixeln (auch für ausgeblendete Zeiger).</summary>
    Rect GetCursorBounds(CursorAnnotation annotation);
}

/// <summary>
/// Standard-Maße über <see cref="TextLayout"/> und die Zeigerbilder (aufgenommen bzw. Windows-Standard).
/// </summary>
public sealed class AnnotationMeasure : IAnnotationMeasure {
    /// <summary>Ersatzgröße, wenn kein Zeigerbild verfügbar ist.</summary>
    public const int FallbackCursorWidth = 12;
    public const int FallbackCursorHeight = 19;

    private readonly CaptureDocument document;
    private string? cachedPng;
    private CursorImage? capturedCursor;

    public AnnotationMeasure(CaptureDocument document) {
        this.document = document;
    }

    public Rect GetTextBounds(TextAnnotation annotation) {
        return TextLayout.GetBounds(annotation);
    }

    public Rect GetCursorBounds(CursorAnnotation annotation) {
        CursorImage? image = annotation.Kind == CursorKinds.Captured ? getCapturedCursor() : RenderService.GetSystemCursor(annotation.Kind);
        if(image == null) return new Rect(annotation.X, annotation.Y, FallbackCursorWidth, FallbackCursorHeight);
        return new Rect(annotation.X - image.HotspotX, annotation.Y - image.HotspotY, image.Bitmap.PixelWidth, image.Bitmap.PixelHeight);
    }

    private CursorImage? getCapturedCursor() {
        CursorInfo? cursor = document.Cursor;
        if(cursor == null || string.IsNullOrEmpty(cursor.Png)) return null;
        if(ReferenceEquals(cursor.Png, cachedPng) && capturedCursor != null && capturedCursor.HotspotX == cursor.HotspotX && capturedCursor.HotspotY == cursor.HotspotY) return capturedCursor;
        try {
            capturedCursor = new CursorImage(ImageFiles.DecodeBgra32(Convert.FromBase64String(cursor.Png)), cursor.HotspotX, cursor.HotspotY);
            cachedPng = cursor.Png;
        } catch(Exception ex) {
            Log.Error(ex, "Aufgenommenes Zeigerbild konnte nicht gelesen werden");
            capturedCursor = null;
            cachedPng = null;
        }
        return capturedCursor;
    }
}
