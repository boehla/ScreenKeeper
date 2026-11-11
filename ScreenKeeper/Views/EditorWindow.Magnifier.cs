using System.Windows;
using System.Windows.Threading;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Views;

/// <summary>
/// Lupe im Editor verstellen: Mausrad (Fenstermodus: Alt+Rad) = Zoom, +/- = Größe. Die Werte gelten sofort (Lupe wird neu
/// aufgebaut und platziert) und landen in den Einstellungen; gespeichert wird gesammelt nach kurzer Ruhe bzw. beim Schließen.
/// </summary>
public partial class EditorWindow {
    private static readonly TimeSpan magnifierSaveDelay = TimeSpan.FromSeconds(1);

    private DispatcherTimer? magnifierSaveTimer;
    private bool magnifierSavePending;
    private bool altWheelUsed;

    /// <summary>Wird die Lupe gerade angezeigt? Nur dann wirken Rad bzw. +/- auf sie.</summary>
    private bool isMagnifierShown => magnifier.Visibility == Visibility.Visible;

    private void configureMagnifier() {
        AppSettings settings = AppServices.Settings.Settings;
        int zoom = Math.Clamp(settings.MagnifierZoom, AppSettings.MinMagnifierZoom, AppSettings.MaxMagnifierZoom);
        magnifier.Configure(original, zoom, settings.MagnifierSize, deviceScale);
        magnifier.SetKeyHints(settings.ShowMagnifierKeyHints ? MagnifierSteps.FormatKeyHints(mode == EditorMode.Windowed, zoom, settings.MagnifierSize) : null);
    }

    /// <summary>Lupen-Zoom um einen Schritt ändern (<paramref name="direction"/> &gt; 0 = mehr).</summary>
    private bool changeMagnifierZoom(int direction) {
        AppSettings settings = AppServices.Settings.Settings;
        int zoom = MagnifierSteps.NextZoom(settings.MagnifierZoom, direction);
        if(zoom == settings.MagnifierZoom) return false;
        settings.MagnifierZoom = zoom;
        applyMagnifierChange();
        return true;
    }

    /// <summary>Lupengröße um einen 10er-Schritt ändern (<paramref name="direction"/> &gt; 0 = größer).</summary>
    private bool changeMagnifierSize(int direction) {
        AppSettings settings = AppServices.Settings.Settings;
        int size = MagnifierSteps.NextSize(settings.MagnifierSize, direction);
        if(size == settings.MagnifierSize) return false;
        settings.MagnifierSize = size;
        applyMagnifierChange();
        return true;
    }

    /// <summary>
    /// Lupe mit den neuen Werten aufbauen und sofort neu platzieren (größere Lupe ragt sonst über den Monitorrand), dann
    /// das Speichern vormerken.
    /// </summary>
    private void applyMagnifierChange() {
        configureMagnifier();
        updateMagnifier();
        // Layout jetzt nachziehen und erneut platzieren: Measure allein liefert bei geänderten Kindern (Linse, Hilfezeile)
        // noch die alte Größe, die Lupe soll aber ohne Mausbewegung richtig sitzen
        magnifier.UpdateLayout();
        updateMagnifier();
        scheduleMagnifierSave();
    }

    private void scheduleMagnifierSave() {
        magnifierSavePending = true;
        if(magnifierSaveTimer == null) {
            magnifierSaveTimer = new DispatcherTimer { Interval = magnifierSaveDelay };
            magnifierSaveTimer.Tick += (s, e) => {
                magnifierSaveTimer.Stop();
                saveMagnifierSettings();
            };
        }
        // jeder weitere Schritt schiebt das Speichern hinaus (gesammelt nach ca. 1 s Ruhe)
        magnifierSaveTimer.Stop();
        magnifierSaveTimer.Start();
    }

    /// <summary>
    /// Nur für automatisierte Tests (<c>--open &lt;json&gt; --test-magnifier</c>): Lupen-Zoom und -Größe über dieselben Wege
    /// wie Rad und +/- je einen Schritt ändern, Tastenhilfe und Größe der Lupe prüfen, dann „Letzten Bereich“ mit einem
    /// Bereich in der Bildmitte übernehmen (ein Rückgängig-Schritt). Speichert keine Einstellungen; Ausgabe auf stdout.
    /// </summary>
    public void RunTestMagnifier() {
        testMode = true;
        DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (s, e) => {
            timer.Stop();
            try {
                runMagnifierScript();
            } catch(Exception ex) {
                TestFailed = true;
                Log.Error(ex, "Test: Lupe fehlgeschlagen");
                Console.Error.WriteLine(ex.Message);
            }
            if(!closed) Close();
        };
        timer.Start();
    }

    private void runMagnifierScript() {
        AppSettings settings = AppServices.Settings.Settings;
        int zoomBefore = settings.MagnifierZoom;
        int sizeBefore = settings.MagnifierSize;
        int zoomDirection = zoomBefore >= AppSettings.MaxMagnifierZoom ? -1 : 1;
        int sizeDirection = sizeBefore >= SettingsViewModel.MagnifierSizeMax ? -1 : 1;
        bool zoomChanged = changeMagnifierZoom(zoomDirection);
        bool sizeChanged = changeMagnifierSize(sizeDirection);
        string expectedHints = settings.ShowMagnifierKeyHints ? MagnifierSteps.FormatKeyHints(mode == EditorMode.Windowed, settings.MagnifierZoom, settings.MagnifierSize) : "";
        bool hintsOk = magnifier.KeyHints == expectedHints;
        bool pending = magnifierSavePending;
        Console.Out.WriteLine($"zoom={zoomBefore}->{settings.MagnifierZoom} size={sizeBefore}->{settings.MagnifierSize} changed={zoomChanged && sizeChanged} pendingSave={pending}");
        Console.Out.WriteLine($"hints=\"{magnifier.KeyHints}\" hintsOk={hintsOk} mode={mode}");

        PixelRect image = viewModel.ImageBounds;
        PixelRect wanted = new PixelRect(image.Width / 4, image.Height / 4, Math.Max(1, image.Width / 2), Math.Max(1, image.Height / 2));
        int undoBefore = viewModel.UndoStack.UndoCount;
        viewModel.LastSelection = LastSelectionRules.ToScreen(wanted, document.VirtualScreen);
        LastSelectionState state = viewModel.LastSelectionState;
        bool canApply = viewModel.ApplyLastSelectionCommand.CanExecute(null);
        if(canApply) viewModel.ApplyLastSelectionCommand.Execute(null);
        bool selectionOk = !canApply || viewModel.Selection == wanted;
        Console.Out.WriteLine($"lastSelection state={state} applied={canApply} selectionOk={selectionOk} undoSteps={viewModel.UndoStack.UndoCount - undoBefore}");

        // im Testmodus nichts speichern, Werte im Speicher zurücksetzen
        settings.MagnifierZoom = zoomBefore;
        settings.MagnifierSize = sizeBefore;
        magnifierSavePending = false;
        magnifierSaveTimer?.Stop();
        bool expectedApplied = mode == EditorMode.Fullscreen;
        if(!zoomChanged || !sizeChanged || !pending || !hintsOk || !selectionOk || canApply != expectedApplied || (canApply && viewModel.UndoStack.UndoCount - undoBefore != 1)) {
            TestFailed = true;
            Console.Error.WriteLine("Test: Lupe bzw. letzter Bereich nicht wie erwartet");
        }
    }

    /// <summary>Vorgemerkte Lupen-Werte speichern (nicht im Testmodus – der soll die echten Einstellungen nicht ändern).</summary>
    private void saveMagnifierSettings() {
        magnifierSaveTimer?.Stop();
        if(!magnifierSavePending) return;
        magnifierSavePending = false;
        if(testMode || warmUp) return;
        try {
            AppServices.Settings.Save();
            AppSettings settings = AppServices.Settings.Settings;
            Log.Info($"Lupe: Zoom {settings.MagnifierZoom}, Größe {settings.MagnifierSize} gespeichert");
        } catch(Exception ex) {
            Log.Error(ex, "Lupen-Einstellungen konnten nicht gespeichert werden");
        }
    }
}
