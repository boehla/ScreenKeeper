using System.Text;

namespace ScreenKeeper.Models;

/// <summary>
/// Ergebnis der Texterkennung (OCR) mit Lage der Zeilen und Wörter. Gilt nur im Arbeitsspeicher (Editor, Werkzeug „Text
/// auswählen“); in der JSON steht davon nur der Text (<see cref="CaptureDocument.OcrText"/>). Alle Rechtecke in
/// Bildpixeln des Original-PNGs (wie die übrige Geometrie), nicht relativ zum Bereich. <see cref="Region"/> ist der
/// Bildausschnitt, auf dem die Erkennung lief (der Bereich bzw. ohne Bereich das ganze Bild).
/// </summary>
public sealed class RecognizedText {
    /// <summary>Bildausschnitt, auf dem die Erkennung lief (Bildpixel).</summary>
    public PixelRect Region { get; set; }

    /// <summary>Sprache der Erkennung als BCP-47-Tag, z. B. <c>de-DE</c>.</summary>
    public string Language { get; set; } = "";

    /// <summary>Erkannte Zeilen in Lesereihenfolge.</summary>
    public List<RecognizedLine> Lines { get; set; } = new List<RecognizedLine>();

    /// <summary>Gibt es mindestens ein Wort?</summary>
    public bool HasWords => Lines.Any(l => l.Words.Count > 0);

    /// <summary>Text aller Zeilen, durch <paramref name="newLine"/> getrennt (Standard <c>\n</c>, wie in <c>ocrText</c>).</summary>
    public string GetText(string newLine = "\n") {
        StringBuilder text = new StringBuilder();
        for(int i = 0; i < Lines.Count; i++) {
            if(i > 0) text.Append(newLine);
            text.Append(Lines[i].Text);
        }
        return text.ToString();
    }
}

/// <summary>Eine erkannte Zeile: Text, umschließendes Rechteck (Bildpixel) und ihre Wörter.</summary>
public sealed class RecognizedLine {
    public string Text { get; set; } = "";

    public PixelRect Bounds { get; set; }

    public List<RecognizedWord> Words { get; set; } = new List<RecognizedWord>();
}

/// <summary>Ein erkanntes Wort mit Rechteck in Bildpixeln.</summary>
public sealed class RecognizedWord {
    public string Text { get; set; } = "";

    public PixelRect Bounds { get; set; }
}
