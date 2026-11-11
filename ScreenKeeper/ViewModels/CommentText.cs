using System.Text;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Darstellung eines Kommentars in Zeilen und Tooltips.
/// </summary>
public static class CommentText {
    /// <summary>Höchstlänge in einer Zeile der History.</summary>
    public const int LineLength = 120;

    /// <summary>Höchstlänge in Tooltips (der Dialog zeigt den ganzen Text).</summary>
    public const int ToolTipLength = 600;

    /// <summary>
    /// Einzeilig: Zeilenumbrüche und mehrfacher Leerraum werden zu einem Leerzeichen, danach auf
    /// <paramref name="maxLength"/> Zeichen gekürzt (…).
    /// </summary>
    public static string SingleLine(string? comment, int maxLength) {
        if(string.IsNullOrWhiteSpace(comment)) return "";
        StringBuilder text = new StringBuilder(comment.Length);
        bool space = false;
        foreach(char c in comment.Trim()) {
            if(char.IsWhiteSpace(c)) {
                space = true;
                continue;
            }
            if(space && text.Length > 0) text.Append(' ');
            space = false;
            text.Append(c);
        }
        return shorten(text.ToString(), maxLength);
    }

    /// <summary>Mehrzeilig (Zeilen bleiben erhalten), auf <paramref name="maxLength"/> Zeichen gekürzt (…).</summary>
    public static string Preview(string? comment, int maxLength) {
        if(string.IsNullOrWhiteSpace(comment)) return "";
        return shorten(comment.Trim(), maxLength);
    }

    private static string shorten(string text, int maxLength) {
        if(text.Length <= maxLength) return text;
        return text.Substring(0, Math.Max(1, maxLength - 1)).TrimEnd() + "…";
    }
}
