using ScreenKeeper.Models;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Ziehen eines Anfassers: Je Achse gibt es einen festen Pixel (gegenüberliegende Kante) und einen bewegten Pixel
/// (die gezogene Kante). Der Bereich ist immer das normalisierte Rechteck aus beiden – zieht man über die
/// gegenüberliegende Kante hinaus, klappt er um. Der Abstand zwischen Mauspixel und Kante beim Greifen bleibt erhalten.
/// </summary>
public readonly struct HandleDrag {
    private readonly int anchorX;
    private readonly int anchorY;
    private readonly int movingX;
    private readonly int movingY;
    private readonly int offsetX;
    private readonly int offsetY;
    private readonly bool moveX;
    private readonly bool moveY;

    private HandleDrag(SelectionHandle handle, int anchorX, int anchorY, int movingX, int movingY, int offsetX, int offsetY, bool moveX, bool moveY) {
        Handle = handle;
        this.anchorX = anchorX;
        this.anchorY = anchorY;
        this.movingX = movingX;
        this.movingY = movingY;
        this.offsetX = offsetX;
        this.offsetY = offsetY;
        this.moveX = moveX;
        this.moveY = moveY;
    }

    public SelectionHandle Handle { get; }

    public static HandleDrag Begin(PixelRect rect, SelectionHandle handle, PixelPoint pointer) {
        bool left = handle == SelectionHandle.TopLeft || handle == SelectionHandle.Left || handle == SelectionHandle.BottomLeft;
        bool right = handle == SelectionHandle.TopRight || handle == SelectionHandle.Right || handle == SelectionHandle.BottomRight;
        bool top = handle == SelectionHandle.TopLeft || handle == SelectionHandle.Top || handle == SelectionHandle.TopRight;
        bool bottom = handle == SelectionHandle.BottomLeft || handle == SelectionHandle.Bottom || handle == SelectionHandle.BottomRight;
        int anchorX = left ? rect.Right - 1 : rect.X;
        int movingX = left ? rect.X : rect.Right - 1;
        int anchorY = top ? rect.Bottom - 1 : rect.Y;
        int movingY = top ? rect.Y : rect.Bottom - 1;
        bool moveX = left || right;
        bool moveY = top || bottom;
        int offsetX = moveX ? movingX - pointer.X : 0;
        int offsetY = moveY ? movingY - pointer.Y : 0;
        return new HandleDrag(handle, anchorX, anchorY, movingX, movingY, offsetX, offsetY, moveX, moveY);
    }

    /// <summary>
    /// Neuer Bereich für die Mausposition <paramref name="pointer"/>; <c>Focus</c> ist der Pixel an der gezogenen Kante
    /// (für die Lupe), auf der nicht gezogenen Achse die Mausposition.
    /// </summary>
    public (PixelRect Rect, PixelPoint Focus) Update(PixelPoint pointer, PixelRect bounds) {
        PixelPoint clampedPointer = SelectionGeometry.ClampPoint(pointer, bounds);
        int x = moveX ? Math.Clamp(pointer.X + offsetX, bounds.X, bounds.Right - 1) : movingX;
        int y = moveY ? Math.Clamp(pointer.Y + offsetY, bounds.Y, bounds.Bottom - 1) : movingY;
        PixelRect rect = SelectionGeometry.FromPoints(new PixelPoint(anchorX, anchorY), new PixelPoint(x, y));
        PixelPoint focus = new PixelPoint(moveX ? x : clampedPointer.X, moveY ? y : clampedPointer.Y);
        return (rect, focus);
    }
}
