using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ScreenKeeper.Models;

namespace ScreenKeeper.Services;

/// <summary>
/// Fertig angeordneter Text: formatierter Text, wirksame Schriftgröße, Innenabstand, Textfeld und Zeichenpunkt (alles in
/// Bildpixeln). <see cref="IsField"/>: festes Feld, der Text wird auf <see cref="Bounds"/> beschnitten.
/// </summary>
public sealed class TextArrangement {
    public TextArrangement(FormattedText text, double fontSize, double padding, Rect bounds, Point origin, bool isField) {
        Text = text;
        FontSize = fontSize;
        Padding = padding;
        Bounds = bounds;
        Origin = origin;
        IsField = isField;
    }

    public FormattedText Text { get; }

    /// <summary>Wirksame Schriftgröße (bei automatischer Größe die berechnete).</summary>
    public double FontSize { get; }

    public double Padding { get; }

    /// <summary>Das ganze Textfeld inklusive Innenabstand.</summary>
    public Rect Bounds { get; }

    /// <summary>Linke obere Ecke für <see cref="DrawingContext.DrawText"/>.</summary>
    public Point Origin { get; }

    public bool IsField { get; }
}

/// <summary>
/// Gemeinsame Text-Anordnung für Anzeige, Export, Treffertest und Inline-Bearbeitung: Das Textfeld beginnt bei
/// (X, Y), der Text selbst um <see cref="GetPadding"/> eingerückt. Freier Text wächst mit dem Inhalt (mit
/// <see cref="TextAnnotation.Width"/> wird umbrochen); ein festes Feld (<see cref="IsField"/>) hat die Größe
/// <see cref="TextAnnotation.Width"/> × <see cref="TextAnnotation.Height"/>, der Text wird darin umbrochen, nach
/// <see cref="TextAnnotation.Alignment"/> ausgerichtet und (beim Zeichnen) abgeschnitten.
/// Schriftgröße in Bildpixeln, <c>PixelsPerDip</c> fest 1 (Bildpixel-Koordinaten).
/// </summary>
public static class TextLayout {
    public const string DefaultFontFamily = "Segoe UI";

    /// <summary>Kleinste bzw. größte Schriftgröße (eigene Eingabe und automatische Größe).</summary>
    public const double MinFontSize = 6;
    public const double MaxFontSize = 400;

    /// <summary>Raster der automatischen Schriftgröße in Bildpixeln.</summary>
    public const double AutoFontSizeStep = 0.5;

    private const int maxCachedAutoSizes = 512;

    private static readonly CultureInfo culture = CultureInfo.GetCultureInfo("de-DE");
    private static readonly Dictionary<string, FontFamily> families = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string Text, string Family, bool Bold, int Width, int Height), double> autoSizes = new Dictionary<(string Text, string Family, bool Bold, int Width, int Height), double>();
    private static readonly object sync = new object();

    /// <summary>Innenabstand zwischen Rand des Textfelds (bzw. Hintergrund) und Text.</summary>
    public static double GetPadding(double fontSize) {
        return Math.Round(Math.Max(1, fontSize) * 0.2);
    }

    public static FontFamily GetFontFamily(string? name) {
        string key = string.IsNullOrWhiteSpace(name) ? DefaultFontFamily : name;
        lock(sync) {
            if(!families.TryGetValue(key, out FontFamily? family)) {
                family = new FontFamily(key);
                families[key] = family;
            }
            return family;
        }
    }

    public static Typeface GetTypeface(TextAnnotation annotation) {
        return new Typeface(GetFontFamily(annotation.FontFamily), FontStyles.Normal, annotation.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
    }

    /// <summary>Festes Textfeld (Breite und Höhe gesetzt)? Sonst freier Text wie bis 0.9.</summary>
    public static bool IsField(TextAnnotation annotation) {
        return annotation.Width.HasValue && annotation.Width.Value > 0 && annotation.Height.HasValue && annotation.Height.Value > 0;
    }

    /// <summary>Zeilenausrichtung nach <see cref="TextAnnotation.Alignment"/>.</summary>
    public static TextAlignment GetTextAlignment(TextAnnotation annotation) {
        switch(TextAlignments.GetColumn(annotation.Alignment)) {
            case 1: return TextAlignment.Center;
            case 2: return TextAlignment.Right;
        }
        return TextAlignment.Left;
    }

    /// <summary>
    /// Wirksame Schriftgröße: bei festem Feld mit <see cref="TextAnnotation.AutoFontSize"/> die berechnete (siehe
    /// <see cref="FindAutoFontSize"/>), sonst <see cref="TextAnnotation.FontSize"/>.
    /// </summary>
    public static double GetEffectiveFontSize(TextAnnotation annotation) {
        if(annotation.AutoFontSize && IsField(annotation)) return FindAutoFontSize(annotation);
        return Math.Max(1, annotation.FontSize);
    }

    /// <summary>
    /// Größte Schriftgröße im Raster <see cref="AutoFontSizeStep"/> zwischen <see cref="MinFontSize"/> und
    /// min(<see cref="MaxFontSize"/>, Feldhöhe), bei der der umbrochene Text (ohne Wörter zu trennen) mitsamt Innenabstand
    /// ins Feld passt; binäre Suche, Ergebnis je Text/Schrift/Feldgröße zwischengespeichert. Passt er auch bei der
    /// Mindestgröße nicht, bleibt es bei <see cref="MinFontSize"/> (der Rest wird abgeschnitten).
    /// </summary>
    public static double FindAutoFontSize(TextAnnotation annotation) {
        int width = annotation.Width ?? 0;
        int height = annotation.Height ?? 0;
        string text = getMeasuredText(annotation);
        string family = string.IsNullOrWhiteSpace(annotation.FontFamily) ? DefaultFontFamily : annotation.FontFamily;
        (string Text, string Family, bool Bold, int Width, int Height) key = (text, family, annotation.Bold, width, height);
        lock(sync) {
            if(autoSizes.TryGetValue(key, out double cached)) return cached;
        }
        Typeface typeface = GetTypeface(annotation);
        double upper = Math.Min(MaxFontSize, height);
        int steps = (int)Math.Floor((upper - MinFontSize) / AutoFontSizeStep);
        int low = 0;
        if(steps > 0 && fits(text, typeface, MinFontSize, width, height)) {
            // low passt immer, gesucht ist der größte passende Schritt
            int high = steps;
            while(low < high) {
                int middle = (low + high + 1) / 2;
                if(fits(text, typeface, MinFontSize + middle * AutoFontSizeStep, width, height)) {
                    low = middle;
                } else {
                    high = middle - 1;
                }
            }
        }
        double result = MinFontSize + low * AutoFontSizeStep;
        lock(sync) {
            if(autoSizes.Count >= maxCachedAutoSizes) autoSizes.Clear();
            autoSizes[key] = result;
        }
        return result;
    }

    /// <summary>
    /// Formatierter Text mit wirksamer Schriftgröße, Umbruchbreite und Zeilenausrichtung; leerer Text wird als ein
    /// Leerzeichen gemessen (damit das Feld eine Zeilenhöhe hat).
    /// </summary>
    public static FormattedText Create(TextAnnotation annotation, Brush foreground) {
        return Arrange(annotation, foreground).Text;
    }

    /// <summary>
    /// Text anordnen: festes Feld → Bounds = (X, Y, Width, Height), Text im Feld umbrochen und nach
    /// <see cref="TextAnnotation.Alignment"/> ausgerichtet (vertikal über den Zeichenpunkt); freier Text → Feld so groß
    /// wie der Text (bzw. Umbruchbreite) plus Innenabstand.
    /// </summary>
    public static TextArrangement Arrange(TextAnnotation annotation, Brush foreground) {
        double fontSize = GetEffectiveFontSize(annotation);
        double padding = GetPadding(fontSize);
        string text = getMeasuredText(annotation);
        TextAlignment alignment = GetTextAlignment(annotation);
        FormattedText formatted = new FormattedText(text, culture, FlowDirection.LeftToRight, GetTypeface(annotation), fontSize, foreground, 1.0);
        if(IsField(annotation)) {
            int width = annotation.Width!.Value;
            int height = annotation.Height!.Value;
            formatted.MaxTextWidth = Math.Max(1, width - 2 * padding);
            formatted.TextAlignment = alignment;
            double free = height - 2 * padding - formatted.Height;
            double y = annotation.Y + padding + free * TextAlignments.GetRow(annotation.Alignment) / 2;
            return new TextArrangement(formatted, fontSize, padding, new Rect(annotation.X, annotation.Y, width, height), new Point(annotation.X + padding, y), true);
        }
        double textWidth;
        if(annotation.Width.HasValue && annotation.Width.Value > 0) {
            formatted.MaxTextWidth = annotation.Width.Value;
            formatted.TextAlignment = alignment;
            textWidth = annotation.Width.Value;
        } else {
            textWidth = formatted.WidthIncludingTrailingWhitespace;
            if(alignment != TextAlignment.Left) {
                // ohne Umbruchbreite richtet WPF nicht innerhalb der Textbreite aus: die breiteste Zeile als Breite vorgeben
                // (aufgerundet, damit nichts zusätzlich umbricht)
                formatted.MaxTextWidth = Math.Ceiling(textWidth) + 1;
                formatted.TextAlignment = alignment;
            }
        }
        Rect bounds = new Rect(annotation.X, annotation.Y, Math.Ceiling(textWidth + 2 * padding), Math.Ceiling(formatted.Height + 2 * padding));
        return new TextArrangement(formatted, fontSize, padding, bounds, new Point(annotation.X + padding, annotation.Y + padding), false);
    }

    /// <summary>
    /// Das ganze Textfeld (inkl. Innenabstand) in Bildpixeln.
    /// </summary>
    public static Rect GetBounds(TextAnnotation annotation) {
        return Arrange(annotation, Brushes.Black).Bounds;
    }

    private static string getMeasuredText(TextAnnotation annotation) {
        return string.IsNullOrEmpty(annotation.Text) ? " " : annotation.Text;
    }

    /// <summary>Passt der Text bei <paramref name="fontSize"/> (mit Innenabstand) ins Feld, ohne Wörter zu trennen?</summary>
    private static bool fits(string text, Typeface typeface, double fontSize, int width, int height) {
        double padding = GetPadding(fontSize);
        double innerWidth = width - 2 * padding;
        double innerHeight = height - 2 * padding;
        if(innerWidth < 1 || innerHeight < 1) return false;
        FormattedText formatted = new FormattedText(text, culture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, 1.0) {
            MaxTextWidth = innerWidth
        };
        // kleine Zugabe gegen Rundungsfehler der Textmessung
        return formatted.Height <= innerHeight + 0.01 && formatted.MinWidth <= innerWidth + 0.01;
    }
}
