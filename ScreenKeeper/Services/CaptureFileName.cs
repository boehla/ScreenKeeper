using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ScreenKeeper.Resources;

namespace ScreenKeeper.Services;

/// <summary>
/// Dateinamen-Muster mit den Platzhaltern {yyyy} {MM} {dd} {HH} {mm} {ss}: Zeit aus einem Dateinamen zurücklesen und
/// Muster prüfen.
/// </summary>
public static class CaptureFileName {
    /// <summary>Die erlaubten Platzhalter.</summary>
    public static IReadOnlyList<string> Placeholders { get; } = new[] { "{yyyy}", "{MM}", "{dd}", "{HH}", "{mm}", "{ss}" };

    private static readonly Dictionary<string, Regex?> cache = new Dictionary<string, Regex?>(StringComparer.Ordinal);
    private static readonly object sync = new object();

    /// <summary>
    /// Liest die Zeit aus einem Basisnamen, der mit <paramref name="pattern"/> erzeugt wurde (auch mit Kollisionssuffix
    /// <c>_2</c>, <c>_3</c> …). Ohne Jahr, Monat oder Tag im Muster gelingt das nicht; fehlende Uhrzeit-Teile sind 0.
    /// </summary>
    public static bool TryParseTime(string baseName, string pattern, out DateTime time) {
        time = default;
        Regex? regex = getRegex(pattern);
        if(regex == null) return false;
        Match match = regex.Match(baseName);
        if(!match.Success) return false;
        int year = getNumber(match, "y");
        int month = getNumber(match, "M");
        int day = getNumber(match, "d");
        int hour = getNumber(match, "H");
        int minute = getNumber(match, "m");
        int second = getNumber(match, "s");
        if(year < 1 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        if(hour > 23 || minute > 59 || second > 59) return false;
        time = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);
        return true;
    }

    /// <summary>
    /// Prüft ein Muster; liefert eine Fehlermeldung oder null. Leer ist erlaubt (= Standardmuster).
    /// </summary>
    public static string? Validate(string? pattern) {
        if(string.IsNullOrWhiteSpace(pattern)) return null;
        char[] invalid = Path.GetInvalidFileNameChars();
        List<char> found = new List<char>();
        int index = 0;
        while(index < pattern.Length) {
            char c = pattern[index];
            if(c == '{') {
                int end = pattern.IndexOf('}', index);
                if(end < 0) return Strings.Format(Strings.SettingsPatternUnclosed, pattern.Substring(index));
                string token = pattern.Substring(index, end - index + 1);
                if(!Placeholders.Contains(token, StringComparer.Ordinal)) return Strings.Format(Strings.SettingsPatternUnknownPlaceholder, token);
                index = end + 1;
                continue;
            }
            if(c == '}' || Array.IndexOf(invalid, c) >= 0) {
                if(!found.Contains(c)) found.Add(c);
            }
            index++;
        }
        if(found.Count > 0) {
            string chars = string.Join(" ", found.Select(ch => char.IsControl(ch) ? $"0x{(int)ch:X2}" : ch.ToString()));
            return Strings.Format(Strings.SettingsPatternInvalidChars, chars);
        }
        if(pattern.Trim().TrimEnd('.').Length == 0) return Strings.Format(Strings.SettingsPatternInvalidChars, ".");
        return null;
    }

    private static int getNumber(Match match, string group) {
        Group value = match.Groups[group];
        return value.Success ? int.Parse(value.Value, NumberStyles.None, CultureInfo.InvariantCulture) : 0;
    }

    private static Regex? getRegex(string pattern) {
        if(string.IsNullOrWhiteSpace(pattern)) pattern = AppSettings.DefaultFileNamePattern;
        lock(sync) {
            if(cache.TryGetValue(pattern, out Regex? cached)) return cached;
            Regex? regex = buildRegex(pattern);
            if(cache.Count > 32) cache.Clear();
            cache[pattern] = regex;
            return regex;
        }
    }

    /// <summary>
    /// Muster → regulärer Ausdruck; Literale werden wie in <see cref="StorageService.FormatFileName"/> behandelt
    /// (ungültige Zeichen → <c>_</c>, Leerraum am Rand entfernt).
    /// </summary>
    private static Regex? buildRegex(string pattern) {
        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder expression = new StringBuilder("^");
        bool hasYear = false;
        bool hasMonth = false;
        bool hasDay = false;
        HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
        string trimmed = pattern.Trim();
        int index = 0;
        while(index < trimmed.Length) {
            string? group = null;
            int length = 0;
            foreach(string placeholder in Placeholders) {
                if(string.CompareOrdinal(trimmed, index, placeholder, 0, placeholder.Length) == 0) {
                    group = placeholder.Substring(1, 1);
                    length = placeholder.Length;
                    break;
                }
            }
            if(group != null) {
                int digits = group == "y" ? 4 : 2;
                // Wiederholte Platzhalter nur als Ziffern prüfen (die erste Fundstelle zählt)
                if(used.Add(group)) {
                    expression.Append($"(?<{group}>\\d{{{digits}}})");
                } else {
                    expression.Append($"\\d{{{digits}}}");
                }
                hasYear |= group == "y";
                hasMonth |= group == "M";
                hasDay |= group == "d";
                index += length;
                continue;
            }
            char c = trimmed[index];
            if(Array.IndexOf(invalid, c) >= 0) c = '_';
            expression.Append(Regex.Escape(c.ToString()));
            index++;
        }
        if(!hasYear || !hasMonth || !hasDay) return null;
        expression.Append("(?:_\\d+)?$");
        return new Regex(expression.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }
}
