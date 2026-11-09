using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels.Undo;

/// <summary>
/// Fügt ein Zeichenobjekt ein. Der Befehl merkt sich eine Kopie des Objekts zum Zeitpunkt seiner Erzeugung, damit
/// spätere Änderungen am lebenden Objekt den Verlauf nicht verfälschen; „Wiederholen“ fügt eine frische Kopie ein.
/// Steht das Objekt schon in der Liste (Live-Vorschau beim Ziehen), mit <see cref="UndoStack.Push"/> verwenden.
/// </summary>
public sealed class AddAnnotationCommand : EditorCommand {
    private readonly CaptureDocument document;
    private readonly Annotation snapshot;
    private readonly int index;
    private readonly string description;

    /// <param name="index">Position in der Zeichenreihenfolge; -1 = Position des Objekts in der Liste bzw. am Ende</param>
    public AddAnnotationCommand(CaptureDocument document, Annotation annotation, int index = -1, string? description = null) {
        this.document = document;
        int existing = AnnotationList.IndexOf(document, annotation.Id);
        this.index = index >= 0 ? index : existing >= 0 ? existing : document.Annotations.Count;
        snapshot = annotation.Clone();
        this.description = description ?? Strings.UndoAddAnnotation;
    }

    public string AnnotationId => snapshot.Id;

    public override string Description => description;

    public override DocumentChangeKind Changes => DocumentChangeKind.Annotations;

    public override void Do() {
        AnnotationList.Remove(document, snapshot.Id);
        document.Annotations.Insert(Math.Clamp(index, 0, document.Annotations.Count), snapshot.Clone());
    }

    public override void Undo() {
        AnnotationList.Remove(document, snapshot.Id);
    }
}
