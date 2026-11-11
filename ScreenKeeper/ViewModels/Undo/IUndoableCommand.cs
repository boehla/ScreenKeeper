namespace ScreenKeeper.ViewModels.Undo;

/// <summary>
/// Ein rückgängig machbarer Befehl. <see cref="Do"/> wird auch für „Wiederholen“ aufgerufen.
/// </summary>
public interface IUndoableCommand {
    /// <summary>Kurzbeschreibung, z. B. „Bereich ändern“.</summary>
    string Description { get; }

    void Do();

    void Undo();
}

/// <summary>
/// Welche Teile des Dokuments ein Befehl ändert (bestimmt, was die Ansicht neu zeichnen muss).
/// </summary>
[Flags]
public enum DocumentChangeKind {
    None = 0,
    /// <summary>Der gewählte Bereich.</summary>
    Selection = 1,
    /// <summary>Zeichenobjekte (inkl. virtueller Maus) – die Annotation-Zeichnung muss neu aufgebaut werden.</summary>
    Annotations = 2,
    /// <summary>Der Kommentar der Aufnahme.</summary>
    Comment = 4
}

/// <summary>
/// Basisklasse für Befehle auf dem <see cref="Models.CaptureDocument"/>.
/// </summary>
public abstract class EditorCommand : IUndoableCommand {
    public abstract string Description { get; }

    /// <summary>Was der Befehl am Dokument ändert.</summary>
    public abstract DocumentChangeKind Changes { get; }

    public abstract void Do();

    public abstract void Undo();
}
