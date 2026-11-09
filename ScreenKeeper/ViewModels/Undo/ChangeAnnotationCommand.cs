using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels.Undo;

/// <summary>
/// Allgemeine Änderung eines Zeichenobjekts (verschieben, Größe, Farbe, Text, sichtbar …) über Vorher/Nachher-Kopien.
/// Ausführen ersetzt das Objekt mit derselben <see cref="Annotation.Id"/> durch eine frische Kopie des Nachher-Stands,
/// Rückgängig durch eine Kopie des Vorher-Stands. Wurde das Objekt schon live geändert, mit <see cref="UndoStack.Push"/>
/// verwenden.
/// </summary>
public sealed class ChangeAnnotationCommand : EditorCommand {
    private readonly CaptureDocument document;
    private readonly Annotation before;
    private readonly Annotation after;
    private readonly string description;

    public ChangeAnnotationCommand(CaptureDocument document, Annotation before, Annotation after, string? description = null) {
        if(before.Id != after.Id) throw new ArgumentException("Vorher und Nachher müssen dasselbe Objekt sein.", nameof(after));
        this.document = document;
        this.before = before.Clone();
        this.after = after.Clone();
        this.description = description ?? Strings.UndoChangeAnnotation;
    }

    public string AnnotationId => after.Id;

    public override string Description => description;

    public override DocumentChangeKind Changes => DocumentChangeKind.Annotations;

    public override void Do() {
        AnnotationList.Replace(document, after.Clone());
    }

    public override void Undo() {
        AnnotationList.Replace(document, before.Clone());
    }
}
