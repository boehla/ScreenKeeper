using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.Views;

/// <summary>
/// Abfrage der Verzögerung („Aufnahme in … Sekunden“) mit Schnellwahl 2 / 5 / 10 s. Nicht modal, damit ein erneuter
/// Delay-Hotkey den Dialog bestätigen kann (<see cref="Confirm"/>). Enter bzw. „Starten“ = mit dem eingetragenen Wert,
/// Schnellwahl = sofort mit diesem Wert, Esc bzw. Schließen = abbrechen.
/// </summary>
public partial class DelayDialog : Window {
    private readonly int suggestion;
    private bool finished;

    public DelayDialog(int seconds) {
        InitializeComponent();
        suggestion = DelayedCaptureController.ClampSeconds(seconds);
        secondsBox.Text = suggestion.ToString(CultureInfo.CurrentCulture);
        foreach(Button button in new[] { quick2, quick5, quick10 }) button.Content = Strings.Format(Strings.DelayDialogQuick, button.Tag);
        PreviewKeyDown += onPreviewKeyDown;
        Loaded += (s, e) => {
            secondsBox.Focus();
            secondsBox.SelectAll();
        };
        Closed += (s, e) => {
            if(finished) return;
            finished = true;
            Cancelled?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Starten mit dieser Verzögerung (Sekunden, bereits geklemmt); der Dialog ist dann schon geschlossen.</summary>
    public event EventHandler<int>? Confirmed;

    /// <summary>Abgebrochen (Esc, Abbrechen, Schließen).</summary>
    public event EventHandler? Cancelled;

    /// <summary>Mit dem eingetragenen Wert starten (ungültige Eingabe → Vorschlag).</summary>
    public void Confirm() {
        confirm(parse());
    }

    /// <summary>Ohne Ereignis schließen (der Ablauf wurde anderweitig abgebrochen).</summary>
    public void Abort() {
        finished = true;
        Close();
    }

    private int parse() {
        if(int.TryParse(secondsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int seconds)) return DelayedCaptureController.ClampSeconds(seconds);
        return suggestion;
    }

    private void confirm(int seconds) {
        if(finished) return;
        finished = true;
        Close();
        Confirmed?.Invoke(this, seconds);
    }

    private void onQuickClick(object sender, RoutedEventArgs e) {
        if(sender is Button button && int.TryParse(button.Tag as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)) confirm(seconds);
    }

    private void onStartClick(object sender, RoutedEventArgs e) {
        Confirm();
    }

    private void onCancelClick(object sender, RoutedEventArgs e) {
        Close();
    }

    private void onPreviewKeyDown(object sender, KeyEventArgs e) {
        if(e.Key == Key.Escape) {
            e.Handled = true;
            Close();
        }
    }
}
