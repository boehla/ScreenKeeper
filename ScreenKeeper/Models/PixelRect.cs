using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Rechteck in Bildpixeln (ganzzahlig). Rechte/untere Kante sind exklusiv.
/// </summary>
public record struct PixelRect(int X, int Y, int Width, int Height) {
    [JsonIgnore]
    public int Right => X + Width;

    [JsonIgnore]
    public int Bottom => Y + Height;

    [JsonIgnore]
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int x, int y) {
        return x >= X && y >= Y && x < Right && y < Bottom;
    }

    /// <summary>
    /// Schnittmenge zweier Rechtecke; leer (default), wenn sie sich nicht überlappen.
    /// </summary>
    public PixelRect Intersect(PixelRect other) {
        int left = Math.Max(X, other.X);
        int top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);
        if(right <= left || bottom <= top) return default;
        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Verschiebt das Rechteck um dx/dy.
    /// </summary>
    public PixelRect Offset(int dx, int dy) {
        return new PixelRect(X + dx, Y + dy, Width, Height);
    }
}
