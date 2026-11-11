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
    public EditorToolbar() {
        InitializeComponent();
        MouseDown += (s, e) => e.Handled = true;
        MouseUp += (s, e) => e.Handled = true;
        MouseWheel += (s, e) => e.Handled = true;
    }

    /// <summary>Das Menü der Schriftgröße (mit Eingabefeld) wurde geschlossen: Tastaturfokus zurückgeben.</summary>
    public event EventHandler? FocusReturnRequested;

    /// <summary>Alle Auswahlmenüs schließen (z. B. wenn die Leiste versteckt wird).</summary>
    public void ClosePopups() {
        foreach(Popup popup in new[] { colorPopup, thicknessPopup, fillPopup, startCapPopup, endCapPopup, fontSizePopup, alignmentPopup, blockSizePopup, blurPopup, stampSizePopup, cursorKindPopup }) {
            popup.IsOpen = false;
        }
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
