using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Views;

namespace ScreenKeeper.Services;

/// <summary>
/// Ablauf: Aufnahme → Dokument bauen → sofort JSON speichern (PNG und Vorschaubild im Hintergrund) → Editor öffnen.
/// Läuft im UI-Thread.
/// </summary>
public sealed class CaptureWorkflow {
    private readonly List<Task> pendingWrites = new List<Task>();
    private readonly object sync = new object();
    private EditorWindow? openEditor;

    /// <summary>Das gerade offene Editor-Fenster (es gibt höchstens eins).</summary>
    public EditorWindow? OpenEditor => openEditor;

    /// <summary>Wird ausgelöst, wenn das Hintergrund-Schreiben eines PNGs fehlschlägt (Pfad, Fehler).</summary>
    public event EventHandler<(string Path, Exception Error)>? WriteFailed;

    /// <summary>Eine neue Aufnahme ist vollständig gespeichert (PNG geschrieben); im UI-Thread.</summary>
    public event EventHandler<CaptureFiles>? CaptureStored;

    /// <summary>Ein Editor wurde geschlossen (Bearbeitungsstand und Vorschaubild sind gespeichert); Argument = JSON-Pfad.</summary>
    public event EventHandler<string>? EditorClosed;

    /// <summary>
    /// Aufnahme machen und sofort speichern. Ergänzt den aufgenommenen Zeiger als virtuelle Maus.
    /// </summary>
    public (CaptureResult Result, SavedCapture Saved) CaptureAndSave() {
        Stopwatch watch = Stopwatch.StartNew();
        CaptureResult result = AppServices.Capture.Capture();
        long captureMs = watch.ElapsedMilliseconds;
        addCapturedCursor(result.Document);
        SavedCapture saved = AppServices.Storage.SaveNewCapture(result.Document, result.Bitmap);
        track(saved);
        Log.Info($"Aufnahme {result.Bitmap.PixelWidth}×{result.Bitmap.PixelHeight}, {result.Document.Monitors.Count} Monitor(e), {result.Document.Windows.Count} Fenster: Aufnahme {captureMs} ms ({result.Timing}), gesamt {watch.ElapsedMilliseconds} ms → {saved.Files.JsonPath}");
        return (result, saved);
    }

    /// <summary>
    /// Aufnahme + Editor (Vollbild). Ist bereits ein Editor offen, wird nur dieser nach vorne geholt.
    /// </summary>
    public EditorWindow CaptureAndEdit() {
        if(openEditor != null) {
            openEditor.BringToFront();
            return openEditor;
        }
        // Zeitmessung bis zum sichtbaren Editor (Log in EditorWindow.ContentRendered)
        long start = Stopwatch.GetTimestamp();
        (CaptureResult result, SavedCapture saved) = CaptureAndSave();
        return ShowEditor(result.Document, result.Bitmap, saved.Files.JsonPath, EditorMode.Fullscreen, start);
    }

    /// <summary>
    /// Vorhandene Aufnahme bearbeiten (wie aus der History): Vollbild, wenn die Monitor-Anordnung gleich ist, sonst Fenster.
    /// </summary>
    public EditorWindow OpenExisting(string jsonPath) {
        if(openEditor != null) openEditor.Close();
        string fullPath = Path.GetFullPath(jsonPath);
        CaptureDocument document = AppServices.Storage.LoadDocument(fullPath);
        BitmapSource original = AppServices.Storage.LoadOriginal(AppServices.Storage.ResolveImagePath(document, fullPath));
        EditorMode mode = AppServices.Capture.IsSameMonitorLayout(document) ? EditorMode.Fullscreen : EditorMode.Windowed;
        return ShowEditor(document, original, fullPath, mode);
    }

    /// <summary>
    /// Aufnahme aus der History bearbeiten: wie <see cref="OpenExisting"/>, aber auch für ein PNG ohne (oder mit defekter)
    /// JSON – dann wird zuerst ein neues Dokument angelegt und gespeichert.
    /// </summary>
    public EditorWindow OpenOrCreate(string pngOrJsonPath) {
        if(openEditor != null) openEditor.Close();
        OpenedCapture opened = AppServices.Storage.OpenOrCreate(pngOrJsonPath);
        BitmapSource original = AppServices.Storage.LoadOriginal(AppServices.Storage.ResolveImagePath(opened.Document, opened.JsonPath));
        EditorMode mode = AppServices.Capture.IsSameMonitorLayout(opened.Document) ? EditorMode.Fullscreen : EditorMode.Windowed;
        return ShowEditor(opened.Document, original, opened.JsonPath, mode);
    }

    /// <summary>
    /// Editor zeigen. <paramref name="startTimestamp"/> (<see cref="Stopwatch.GetTimestamp"/>) = Beginn der Zeitmessung bis
    /// zum sichtbaren Editor; 0 = ab jetzt.
    /// </summary>
    public EditorWindow ShowEditor(CaptureDocument document, BitmapSource original, string jsonPath, EditorMode mode, long startTimestamp = 0) {
        EditorWindow editor = new EditorWindow(document, original, jsonPath, mode, startTimestamp);
        openEditor = editor;
        editor.Closed += (s, e) => {
            if(ReferenceEquals(openEditor, editor)) openEditor = null;
            EditorClosed?.Invoke(this, jsonPath);
        };
        editor.Show();
        editor.Activate();
        return editor;
    }

    /// <summary>
    /// Wartet (höchstens <paramref name="timeout"/>) auf laufende PNG-Schreibvorgänge, z. B. vor dem Beenden.
    /// </summary>
    public void WaitForPendingWrites(TimeSpan timeout) {
        Task[] tasks;
        lock(sync) {
            tasks = pendingWrites.ToArray();
        }
        if(tasks.Length == 0) return;
        try {
            if(!Task.WaitAll(tasks, timeout)) Log.Warn("Nicht alle Bilder wurden rechtzeitig geschrieben.");
        } catch(AggregateException ex) {
            Log.Error(ex, "Fehler beim Schreiben der Bilder");
        }
    }

    private void addCapturedCursor(CaptureDocument document) {
        CursorInfo? cursor = document.Cursor;
        if(cursor == null || string.IsNullOrEmpty(cursor.Png) || !cursor.CapturedVisible) return;
        document.Annotations.Add(new CursorAnnotation {
            X = cursor.X,
            Y = cursor.Y,
            Kind = CursorKinds.Captured,
            Visible = AppServices.Settings.Settings.CursorVisible
        });
    }

    private void track(SavedCapture saved) {
        Task writes = Task.WhenAll(saved.PngTask, saved.ThumbnailTask);
        lock(sync) {
            pendingWrites.Add(writes);
        }
        writes.ContinueWith(task => {
            lock(sync) {
                pendingWrites.Remove(task);
            }
            if(saved.ThumbnailTask.Exception != null) {
                // nicht kritisch: die History erzeugt fehlende Vorschaubilder selbst
                Log.Error(saved.ThumbnailTask.Exception.GetBaseException(), $"Vorschaubild {saved.Files.ThumbPath} konnte nicht geschrieben werden");
            }
            if(saved.PngTask.Exception != null) {
                Exception error = saved.PngTask.Exception.GetBaseException();
                Log.Error(error, $"PNG {saved.Files.PngPath} konnte nicht geschrieben werden");
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => WriteFailed?.Invoke(this, (saved.Files.PngPath, error))));
                return;
            }
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => CaptureStored?.Invoke(this, saved.Files)));
        }, TaskScheduler.Default);
    }
}
