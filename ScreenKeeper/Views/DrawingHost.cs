using System.Windows;
using System.Windows.Media;

namespace ScreenKeeper.Views;

/// <summary>
/// Zeigt ein <see cref="Drawing"/> in seinen eigenen Koordinaten an (ohne Verschiebung auf die Drawing-Bounds,
/// wie es ein DrawingImage täte). Damit stimmen Bildpixel-Koordinaten der Annotationen mit dem Original überein.
/// </summary>
public sealed class DrawingHost : FrameworkElement {
    public static readonly DependencyProperty DrawingProperty = DependencyProperty.Register(
        nameof(Drawing), typeof(Drawing), typeof(DrawingHost),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Drawing? Drawing {
        get => (Drawing?)GetValue(DrawingProperty);
        set => SetValue(DrawingProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext) {
        Drawing? drawing = Drawing;
        if(drawing != null) drawingContext.DrawDrawing(drawing);
    }
}
