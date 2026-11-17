using System.Text;
using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Markierung im erkannten Text (Werkzeug „Text auswählen“), reiner Ansichtszustand. Die Wörter werden in Lesereihenfolge
/// (Zeilen, darin Wörter – wie von der Erkennung geliefert) durchnummeriert; markiert ist immer ein zusammenhängender
/// Abschnitt <see cref="Start"/> … <see cref="End"/> (einschließlich), auch über Zeilen hinweg – wie in einem Dokument.
/// </summary>
public sealed class OcrTextSelection {
    private readonly RecognizedText? source;
    private readonly List<WordRef> words = new List<WordRef>();

    private readonly record struct WordRef(int Line, int Index, RecognizedWord Word);

    public OcrTextSelection(RecognizedText? source) {
        this.source = source;
        if(source == null) return;
        for(int line = 0; line < source.Lines.Count; line++) {
            List<RecognizedWord> lineWords = source.Lines[line].Words;
            for(int i = 0; i < lineWords.Count; i++) words.Add(new WordRef(line, i, lineWords[i]));
        }
    }

    /// <summary>Das Erkennungsergebnis, zu dem diese Markierung gehört (null = keins).</summary>
    public RecognizedText? Source => source;

    public int WordCount => words.Count;

    /// <summary>Erstes markiertes Wort (Index in Lesereihenfolge); null = nichts markiert.</summary>
    public int? Start { get; private set; }

    /// <summary>Letztes markiertes Wort (einschließlich); null = nichts markiert.</summary>
    public int? End { get; private set; }

    public bool HasSelection => Start.HasValue && End.HasValue;

    public void Clear() {
        Start = null;
        End = null;
    }

    /// <summary>Markiert alle Wörter von <paramref name="anchor"/> bis <paramref name="current"/> (Reihenfolge egal).</summary>
    public void SelectRange(int anchor, int current) {
        if(words.Count == 0) {
            Clear();
            return;
        }
        int a = Math.Clamp(anchor, 0, words.Count - 1);
        int b = Math.Clamp(current, 0, words.Count - 1);
        Start = Math.Min(a, b);
        End = Math.Max(a, b);
    }

    /// <summary>Alles markieren (ohne Wörter: nichts).</summary>
    public void SelectAll() {
        if(words.Count == 0) {
            Clear();
            return;
        }
        Start = 0;
        End = words.Count - 1;
    }

    /// <summary>Wort, dessen Rechteck (um <paramref name="tolerance"/> Bildpixel vergrößert) den Punkt enthält; sonst null.</summary>
    public int? FindWordAt(double x, double y, double tolerance = 0) {
        int? best = null;
        double bestDistance = double.MaxValue;
        for(int i = 0; i < words.Count; i++) {
            PixelRect bounds = words[i].Word.Bounds;
            double dx = distance(x, bounds.X, bounds.Right);
            double dy = distance(y, bounds.Y, bounds.Bottom);
            if(dx > tolerance || dy > tolerance) continue;
            double d = dx + dy;
            if(d < bestDistance) {
                best = i;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>
    /// Wort, an dem eine Markierung bei diesem Punkt beginnt bzw. endet: zuerst die Zeile (die den Punkt in der Höhe
    /// enthält, bei mehreren die waagerecht nächste; sonst die nächstgelegene), darin das waagerecht nächste Wort.
    /// Null, wenn es keine Wörter gibt.
    /// </summary>
    public int? FindNearestWord(double x, double y) {
        if(words.Count == 0 || source == null) return null;
        int bestLine = -1;
        double bestLineDistance = double.MaxValue;
        bool bestInBand = false;
        for(int line = 0; line < source.Lines.Count; line++) {
            PixelRect bounds = getLineBounds(line);
            if(bounds.IsEmpty) continue;
            double dx = distance(x, bounds.X, bounds.Right);
            double dy = distance(y, bounds.Y, bounds.Bottom);
            bool inBand = dy == 0;
            // Zeilen, die den Punkt in der Höhe enthalten, haben Vorrang; darunter zählt die waagerechte Nähe
            double d = inBand ? dx : Math.Sqrt(dx * dx + dy * dy);
            if(inBand && !bestInBand || inBand == bestInBand && d < bestLineDistance) {
                bestLine = line;
                bestLineDistance = d;
                bestInBand = inBand;
            }
        }
        if(bestLine < 0) return null;
        int? best = null;
        double bestDistance = double.MaxValue;
        for(int i = 0; i < words.Count; i++) {
            if(words[i].Line != bestLine) continue;
            PixelRect bounds = words[i].Word.Bounds;
            double d = distance(x, bounds.X, bounds.Right);
            if(d < bestDistance) {
                best = i;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>
    /// Markierter Text: Wörter einer Zeile durch Leerzeichen getrennt (ist die ganze Zeile markiert, ihr erkannter Text),
    /// Zeilen durch <paramref name="newLine"/>. Leer, wenn nichts markiert ist.
    /// </summary>
    public string GetSelectedText(string newLine = "\r\n") {
        if(!HasSelection || source == null) return "";
        StringBuilder text = new StringBuilder();
        int i = Start!.Value;
        int end = End!.Value;
        bool first = true;
        while(i <= end) {
            int line = words[i].Line;
            int lineStart = i;
            while(i <= end && words[i].Line == line) i++;
            int count = i - lineStart;
            if(!first) text.Append(newLine);
            first = false;
            RecognizedLine recognized = source.Lines[line];
            if(count == recognized.Words.Count && !string.IsNullOrEmpty(recognized.Text)) {
                text.Append(recognized.Text);
            } else {
                text.Append(string.Join(" ", words.GetRange(lineStart, count).Select(w => w.Word.Text)));
            }
        }
        return text.ToString();
    }

    /// <summary>Hervorhebung: je Zeile ein Rechteck um die markierten Wörter (Bildpixel).</summary>
    public IReadOnlyList<PixelRect> GetHighlightRects() {
        List<PixelRect> rects = new List<PixelRect>();
        if(!HasSelection) return rects;
        int i = Start!.Value;
        int end = End!.Value;
        while(i <= end) {
            int line = words[i].Line;
            List<PixelRect> lineRects = new List<PixelRect>();
            while(i <= end && words[i].Line == line) {
                lineRects.Add(words[i].Word.Bounds);
                i++;
            }
            PixelRect union = OcrLayout.Union(lineRects);
            if(!union.IsEmpty) rects.Add(union);
        }
        return rects;
    }

    private PixelRect getLineBounds(int line) {
        RecognizedLine recognized = source!.Lines[line];
        if(recognized.Words.Count == 0) return default;
        return recognized.Bounds.IsEmpty ? OcrLayout.Union(recognized.Words.Select(w => w.Bounds)) : recognized.Bounds;
    }

    /// <summary>Abstand eines Werts zu einem Intervall [<paramref name="from"/>, <paramref name="to"/>] (0 innerhalb).</summary>
    private static double distance(double value, double from, double to) {
        if(value < from) return from - value;
        if(value > to) return value - to;
        return 0;
    }
}
