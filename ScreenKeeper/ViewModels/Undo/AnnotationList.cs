using ScreenKeeper.Models;

namespace ScreenKeeper.ViewModels.Undo;

/// <summary>
/// Zugriff auf <see cref="CaptureDocument.Annotations"/> über die <see cref="Annotation.Id"/> (Befehle ersetzen Objekte
/// durch Kopien, deshalb zählt die Kennung, nicht die Objekt-Referenz).
/// </summary>
public static class AnnotationList {
    public static int IndexOf(CaptureDocument document, string id) {
        for(int i = 0; i < document.Annotations.Count; i++) {
            if(document.Annotations[i].Id == id) return i;
        }
        return -1;
    }

    public static Annotation? Find(CaptureDocument document, string? id) {
        if(string.IsNullOrEmpty(id)) return null;
        int index = IndexOf(document, id);
        return index >= 0 ? document.Annotations[index] : null;
    }

    public static bool Remove(CaptureDocument document, string id) {
        int index = IndexOf(document, id);
        if(index < 0) return false;
        document.Annotations.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Ersetzt das Objekt mit derselben Kennung; gibt es keins, wird es angehängt.
    /// </summary>
    public static void Replace(CaptureDocument document, Annotation annotation) {
        int index = IndexOf(document, annotation.Id);
        if(index >= 0) {
            document.Annotations[index] = annotation;
        } else {
            document.Annotations.Add(annotation);
        }
    }
}
