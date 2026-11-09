using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels.Undo;

/// <summary>
/// Entfernt ein Zeichenobjekt; „Rückgängig“ setzt eine Kopie an der alten Stelle der Zeichenreihenfolge wieder ein.
/// </summary>
public sealed class RemoveAnnotationCommand : EditorCommand {
    private readonly CaptureDocument document;
    private readonly Annotation snapshot;
    private readonly int index;

    public RemoveAnnotationCommand(CaptureDocument document, Annotation annotation) {
        this.document = document;
        int existing = AnnotationList.IndexOf(document, annotation.Id);
        index = existing >= 0 ? existing : document.Annotations.Count;
        snapshot = annotation.Clone();
    }

    public string AnnotationId => snapshot.Id;

    public override string Description => Strings.UndoRemoveAnnotation;

    public override DocumentChangeKind Changes => DocumentChangeKind.Annotations;

    public override void Do() {
        AnnotationList.Remove(document, snapshot.Id);
    }

    public override void Undo() {
        AnnotationList.Remove(document, snapshot.Id);
        document.Annotations.Insert(Math.Clamp(index, 0, document.Annotations.Count), snapshot.Clone());
    }
}
