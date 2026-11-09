using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ScreenKeeper.Models;

namespace ScreenKeeper.Services;

/// <summary>
/// Gemeinsame Text-Anordnung für Anzeige, Export, Treffertest und Inline-Bearbeitung: Das Textfeld beginnt bei
/// (X, Y), der Text selbst um <see cref="GetPadding"/> eingerückt; mit <see cref="TextAnnotation.Width"/> wird umbrochen.
/// Schriftgröße in Bildpixeln, <c>PixelsPerDip</c> fest 1 (Bildpixel-Koordinaten).
/// </summary>
public static class TextLayout {
    public const string DefaultFontFamily = "Segoe UI";

    private static readonly CultureInfo culture = CultureInfo.GetCultureInfo("de-DE");
    private static readonly Dictionary<string, FontFamily> families = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>
    /// Formatierter Text; leerer Text wird als ein Leerzeichen gemessen (damit das Feld eine Zeilenhöhe hat).
    /// </summary>
    public static FormattedText Create(TextAnnotation annotation, Brush foreground) {
        string text = string.IsNullOrEmpty(annotation.Text) ? " " : annotation.Text;
        FormattedText formatted = new FormattedText(text, culture, FlowDirection.LeftToRight, GetTypeface(annotation), Math.Max(1, annotation.FontSize), foreground, 1.0);
        if(annotation.Width.HasValue && annotation.Width.Value > 0) formatted.MaxTextWidth = annotation.Width.Value;
        return formatted;
    }

    /// <summary>
    /// Das ganze Textfeld (inkl. Innenabstand) in Bildpixeln.
    /// </summary>
    public static Rect GetBounds(TextAnnotation annotation) {
        FormattedText formatted = Create(annotation, Brushes.Black);
        return GetBounds(annotation, formatted);
    }

    public static Rect GetBounds(TextAnnotation annotation, FormattedText formatted) {
        double padding = GetPadding(annotation.FontSize);
        double width = annotation.Width.HasValue && annotation.Width.Value > 0 ? annotation.Width.Value : formatted.WidthIncludingTrailingWhitespace;
        return new Rect(annotation.X, annotation.Y, Math.Ceiling(width + 2 * padding), Math.Ceiling(formatted.Height + 2 * padding));
    }
}
