using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.Views;

/// <summary>
/// Eingabefeld mit Tastenaufzeichnung: Feld fokussieren und die Kombination drücken; <see cref="Hotkey"/> erhält den
/// gespeicherten Text (z. B. <c>Ctrl+Shift+PrintScreen</c>). Tab/Shift+Tab wechseln weiter das Feld.
/// Die Druck-Taste meldet Windows nur beim Loslassen, deshalb wird sie im KeyUp ausgewertet.
/// </summary>
public sealed class HotkeyBox : TextBox {
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, onHotkeyChanged));

    public HotkeyBox() {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        IsTabStop = true;
        Cursor = Cursors.Arrow;
        ContextMenu = null;
        InputMethod.SetIsInputMethodEnabled(this, false);
        updateText();
    }

    public string Hotkey {
        get => (string)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    /// <summary>Feld hat den Tastaturfokus: die globalen Hotkeys sind solange ausgesetzt.</summary>
    public bool IsRecording { get; private set; }

    public event EventHandler? RecordingChanged;

    protected override void OnPreviewKeyDown(KeyEventArgs e) {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modifiers = Keyboard.Modifiers;
        if(key == Key.Tab && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift)) {
            base.OnPreviewKeyDown(e);
            return;
        }
        e.Handled = true;
        if(key == Key.Snapshot) return;
        if(modifiers == ModifierKeys.None && key == Key.Escape) {
            // Aufzeichnung abbrechen: Fokus weitergeben, Wert bleibt
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            return;
        }
        if(modifiers == ModifierKeys.None && (key == Key.Back || key == Key.Delete)) {
            Hotkey = "";
            updateText();
            return;
        }
        HotkeyBinding? binding = HotkeyBinding.FromKey(key, modifiers);
        if(binding == null) {
            // nur Modifizierer gedrückt: Zwischenstand zeigen
            Text = describeModifiers(modifiers) + "…";
            return;
        }
        Hotkey = binding.ToString();
        updateText();
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e) {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        if(key == Key.Snapshot) {
            HotkeyBinding? binding = HotkeyBinding.FromKey(Key.Snapshot, Keyboard.Modifiers);
            if(binding != null) Hotkey = binding.ToString();
        }
        if(Keyboard.Modifiers == ModifierKeys.None) updateText();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) {
        base.OnGotKeyboardFocus(e);
        IsRecording = true;
        ToolTip = Strings.SettingsHotkeyRecording;
        RecordingChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) {
        base.OnLostKeyboardFocus(e);
        IsRecording = false;
        updateText();
        RecordingChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void onHotkeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        ((HotkeyBox)d).updateText();
    }

    private void updateText() {
        string display = HotkeyBinding.ToDisplayString(Hotkey);
        Text = display.Length > 0 ? display : Strings.SettingsHotkeyNone;
    }

    private static string describeModifiers(ModifierKeys modifiers) {
        List<string> parts = new List<string>();
        if(modifiers.HasFlag(ModifierKeys.Control)) parts.Add(Strings.KeyCtrl);
        if(modifiers.HasFlag(ModifierKeys.Alt)) parts.Add(Strings.KeyAlt);
        if(modifiers.HasFlag(ModifierKeys.Shift)) parts.Add(Strings.KeyShift);
        if(modifiers.HasFlag(ModifierKeys.Windows)) parts.Add(Strings.KeyWin);
        return parts.Count == 0 ? "" : string.Join("+", parts) + "+";
    }
}
