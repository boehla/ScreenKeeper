using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Views;

/// <summary>
/// Schwebende Werkzeugleiste des Editors (DataContext = <see cref="ViewModels.EditorViewModel"/>).
/// Liegt außerhalb der gezoomten Zeichenfläche; Mausereignisse werden hier abgefangen und erreichen die Fläche nicht.
/// Die Knöpfe sind nicht fokussierbar, damit die Tastaturkürzel beim Fenster (bzw. beim Textfeld) bleiben.
/// Auswahlmenüs (Farbe, Strichstärke …) sind Popups; ein Eintrag ruft <see cref="EditorViewModel.ApplyChoice"/> auf.
/// Einzige Ausnahme ist die Eingabe einer eigenen Schriftgröße: danach gibt <see cref="FocusReturnRequested"/> den
/// Fokus zurück (an das Textfeld bzw. das Fenster).
/// </summary>
public partial class EditorToolbar : UserControl {
    /// <summary>
    /// Feste Breite des Platzes für die Optionen neben „Objekt löschen“: mindestens so breit wie die breiteste
    /// Kombination (Text, siehe Test <c>ToolbarLayoutTests</c>). So ist die Leiste immer gleich breit und kein Knopf
    /// außerhalb wandert, wenn Optionen erscheinen oder verschwinden.
    /// </summary>
    public const double OptionsSlotWidth = 230;

    private readonly Popup[] popups;

    public EditorToolbar() {
        InitializeComponent();
        popups = new[] { colorPopup, thicknessPopup, fillPopup, startCapPopup, endCapPopup, fontSizePopup, alignmentPopup, blockSizePopup, blurPopup, stampSizePopup, cursorKindPopup };
        MouseDown += (s, e) => e.Handled = true;
        MouseUp += (s, e) => e.Handled = true;
        MouseWheel += (s, e) => e.Handled = true;
        MouseLeave += (s, e) => raiseUseEnded();
        foreach(Popup popup in popups) popup.Closed += (s, e) => raiseUseEnded();
    }

    /// <summary>Das Menü der Schriftgröße (mit Eingabefeld) wurde geschlossen: Tastaturfokus zurückgeben.</summary>
    public event EventHandler? FocusReturnRequested;

    /// <summary>Die Maus hat die Leiste verlassen bzw. das letzte Menü wurde geschlossen: die Leiste darf neu platziert werden.</summary>
    public event EventHandler? UseEnded;

    /// <summary>
    /// Wird die Leiste gerade bedient (Maus darüber oder ein Menü offen)? Dann bleibt sie stehen, damit kein Knopf unter
    /// der Maus wegrutscht (z. B. wenn „Rückgängig“ den Bereich ändert).
    /// </summary>
    public bool IsInUse => IsMouseOver || popups.Any(p => p.IsOpen);

    /// <summary>Alle Auswahlmenüs schließen (z. B. wenn die Leiste versteckt wird).</summary>
    public void ClosePopups() {
        foreach(Popup popup in popups) popup.IsOpen = false;
    }

    private void raiseUseEnded() {
        if(!IsInUse) UseEnded?.Invoke(this, EventArgs.Empty);
    }

    private void onChoiceClick(object sender, RoutedEventArgs e) {
        if(sender is FrameworkElement element && element.DataContext is OptionChoice choice && DataContext is EditorViewModel viewModel) {
            viewModel.ApplyChoice(choice);
        }
        ClosePopups();
    }

    private void onFontSizePopupOpened(object? sender, EventArgs e) {
        if(DataContext is EditorViewModel viewModel) fontSizeInput.Text = viewModel.CurrentFontSizeValueText;
        fontSizeInput.SelectAll();
    }

    private void onFontSizePopupClosed(object? sender, EventArgs e) {
        // nur wenn der Fokus im Eingabefeld lag (bzw. mit dem Menü verloren ging); Klicks auf Einträge lassen ihn, wo er ist
        if(fontSizeInput.IsKeyboardFocusWithin || Keyboard.FocusedElement == null) {
            FocusReturnRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Enter übernimmt die eigene Größe (ungültige Eingabe: Feld bleibt offen, Text markiert), Esc schließt.</summary>
    private void onFontSizeInputKeyDown(object sender, KeyEventArgs e) {
        if(e.Key == Key.Escape) {
            e.Handled = true;
            ClosePopups();
            return;
        }
        if(e.Key != Key.Enter) return;
        e.Handled = true;
        if(DataContext is EditorViewModel viewModel && viewModel.ApplyFontSizeInput(fontSizeInput.Text)) {
            ClosePopups();
            return;
        }
        fontSizeInput.SelectAll();
    }
}
