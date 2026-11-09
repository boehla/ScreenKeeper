using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Views;

/// <summary>
/// Schwebende Werkzeugleiste des Editors (DataContext = <see cref="ViewModels.EditorViewModel"/>).
/// Liegt außerhalb der gezoomten Zeichenfläche; Mausereignisse werden hier abgefangen und erreichen die Fläche nicht.
/// Die Knöpfe sind nicht fokussierbar, damit die Tastaturkürzel beim Fenster (bzw. beim Textfeld) bleiben.
/// Auswahlmenüs (Farbe, Strichstärke …) sind Popups; ein Eintrag ruft <see cref="EditorViewModel.ApplyChoice"/> auf.
/// </summary>
public partial class EditorToolbar : UserControl {
    public EditorToolbar() {
        InitializeComponent();
        MouseDown += (s, e) => e.Handled = true;
        MouseUp += (s, e) => e.Handled = true;
        MouseWheel += (s, e) => e.Handled = true;
    }

    /// <summary>Alle Auswahlmenüs schließen (z. B. wenn die Leiste versteckt wird).</summary>
    public void ClosePopups() {
        foreach(Popup popup in new[] { colorPopup, thicknessPopup, fontSizePopup, blockSizePopup, blurPopup, stampSizePopup, cursorKindPopup }) {
            popup.IsOpen = false;
        }
    }

    private void onChoiceClick(object sender, RoutedEventArgs e) {
        if(sender is FrameworkElement element && element.DataContext is OptionChoice choice && DataContext is EditorViewModel viewModel) {
            viewModel.ApplyChoice(choice);
        }
        ClosePopups();
    }
}
