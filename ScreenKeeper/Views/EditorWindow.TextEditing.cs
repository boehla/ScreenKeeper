using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.Views;

/// <summary>
/// Inline-Textbearbeitung: ein Textfeld im Overlay genau über der Stelle des Textes, mit Schrift, Größe (gezoomt),
/// Ausrichtung und Farbe des Objekts. Ein festes Textfeld zeigt seine Feldgröße mit gestricheltem Rahmen, die
/// vertikale Ausrichtung (über den oberen Innenabstand) und bei automatischer Schriftgröße die laufend neu berechnete
/// Größe – alles aus <see cref="TextLayout.Arrange"/>, wie beim Zeichnen.
/// Enter fügt eine Zeile ein, Strg+Enter übernimmt, Esc verwirft; ein Klick daneben übernimmt.
/// </summary>
public partial class EditorWindow {
    // Innenabstand des TextBox-Inhalts (Rahmen 1 + Textansicht 2 DIP), damit der Text an derselben Stelle steht wie gezeichnet
    private const double textBoxInsetX = 3;
    private const double textBoxInsetY = 1;

    // Rahmen eines festen Felds: abwechselnd schwarz/weiß (wie die gestrichelten Hilfslinien), auf jedem Hintergrund sichtbar
    private static readonly Brush fieldBorderBrush = createFieldBorderBrush();

    private bool updatingTextEditor;
    private string? textEditorAnnotationId;
    private Brush? defaultTextEditorBorder;

    private void attachTextEditor() {
        defaultTextEditorBorder = textEditor.BorderBrush;
        viewModel.TextEditChanged += (s, e) => updateTextEditor();
        textEditor.PreviewKeyDown += onTextEditorPreviewKeyDown;
        // Anfasser des festen Felds liegen zur Hälfte über dem Textfeld: dort gehört die Maus dem Größe-Ändern
        textEditor.PreviewMouseLeftButtonDown += (s, e) => {
            if(viewModel.HitEditingTextHandle(toInput(e.GetPosition(viewport))) == ViewModels.ObjectHandle.None) return;
            onViewportLeftButtonDown(viewport, e);
        };
        textEditor.PreviewMouseMove += (s, e) => {
            ViewModels.ObjectHandle handle = viewModel.HitEditingTextHandle(toInput(e.GetPosition(viewport)));
            textEditor.Cursor = handle == ViewModels.ObjectHandle.None ? Cursors.IBeam : toWpfCursor(ViewModels.AnnotationEditing.GetCursor(handle));
        };
        textEditor.TextChanged += (s, e) => {
            if(updatingTextEditor) return;
            viewModel.UpdateEditingText(textEditor.Text);
            // festes Feld: automatische Größe und vertikale Lage hängen vom Text ab
            TextAnnotation? text = viewModel.EditingText;
            if(text != null && TextLayout.IsField(text)) updateTextEditorLayout();
        };
        toolbar.FocusReturnRequested += (s, e) => {
            if(viewModel.IsTextEditing && textEditor.Visibility == Visibility.Visible) {
                textEditor.Focus();
                Keyboard.Focus(textEditor);
            } else {
                Focus();
                Keyboard.Focus(this);
            }
        };
    }

    /// <summary>
    /// Textfeld zeigen, anpassen oder ausblenden (je nach <see cref="ViewModels.EditorViewModel.EditingText"/>).
    /// </summary>
    private void updateTextEditor() {
        TextAnnotation? text = viewModel.EditingText;
        if(text == null) {
            bool hadFocus = textEditor.IsKeyboardFocusWithin;
            textEditorAnnotationId = null;
            textEditor.Visibility = Visibility.Collapsed;
            if(hadFocus || IsActive) {
                Focus();
                Keyboard.Focus(this);
            }
            return;
        }
        bool starting = textEditorAnnotationId != text.Id || textEditor.Visibility != Visibility.Visible;
        updatingTextEditor = true;
        try {
            if(starting) {
                textEditor.Text = text.Text;
                textEditor.CaretIndex = textEditor.Text.Length;
            }
            textEditorAnnotationId = text.Id;
            applyTextEditorStyle(text);
            textEditor.Visibility = Visibility.Visible;
            updateTextEditorLayout();
        } finally {
            updatingTextEditor = false;
        }
        if(starting) {
            textEditor.Focus();
            Keyboard.Focus(textEditor);
        }
    }

    private void applyTextEditorStyle(TextAnnotation text) {
        textEditor.FontFamily = TextLayout.GetFontFamily(text.FontFamily);
        textEditor.FontWeight = text.Bold ? FontWeights.Bold : FontWeights.Normal;
        SolidColorBrush foreground = AnnotationColors.GetBrush(text.Foreground);
        textEditor.Foreground = foreground;
        textEditor.CaretBrush = foreground;
        textEditor.Background = string.IsNullOrEmpty(text.Background) ? Brushes.Transparent : AnnotationColors.GetBrush(text.Background);
        textEditor.TextWrapping = text.Width.HasValue ? TextWrapping.Wrap : TextWrapping.NoWrap;
        textEditor.TextAlignment = TextLayout.GetTextAlignment(text);
        textEditor.BorderBrush = TextLayout.IsField(text) ? fieldBorderBrush : defaultTextEditorBorder;
    }

    /// <summary>
    /// Position und Größe des Textfelds an Zoom/Pan anpassen (auch während der Bearbeitung).
    /// </summary>
    private void updateTextEditorLayout() {
        TextAnnotation? text = viewModel.EditingText;
        if(text == null || textEditor.Visibility != Visibility.Visible) return;
        double zoom = transform.Zoom;
        TextArrangement layout = TextLayout.Arrange(text, Brushes.Black);
        double padding = layout.Padding * zoom;
        double fontSize = Math.Max(1, layout.FontSize * zoom);
        textEditor.FontSize = fontSize;
        if(layout.IsField) {
            // feste Feldgröße; die vertikale Ausrichtung steckt im oberen Innenabstand (läuft der Text über, beginnt er oben)
            double top = Math.Max(0, (layout.Origin.Y - text.Y) * zoom);
            textEditor.Padding = new Thickness(padding, top, padding, 0);
            textEditor.MinWidth = 0;
            textEditor.Width = layout.Bounds.Width * zoom + 2 * textBoxInsetX;
            textEditor.Height = layout.Bounds.Height * zoom + 2 * textBoxInsetY;
        } else {
            textEditor.Padding = new Thickness(padding, padding, padding, padding);
            textEditor.MinWidth = fontSize + 2 * padding + 2 * textBoxInsetX;
            textEditor.Width = text.Width.HasValue ? text.Width.Value * zoom + 2 * padding + 2 * textBoxInsetX : double.NaN;
            textEditor.Height = double.NaN;
        }
        Point topLeft = transform.ToView(text.X, text.Y);
        Canvas.SetLeft(textEditor, topLeft.X - textBoxInsetX);
        Canvas.SetTop(textEditor, topLeft.Y - textBoxInsetY);
    }

    private void onTextEditorPreviewKeyDown(object sender, KeyEventArgs e) {
        if(e.Key == Key.Escape) {
            e.Handled = true;
            viewModel.CancelTextEdit();
            return;
        }
        // Enter fügt eine neue Zeile ein (AcceptsReturn); Strg+Enter übernimmt den Text
        if(e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) {
            e.Handled = true;
            viewModel.UpdateEditingText(textEditor.Text);
            viewModel.CommitTextEdit();
        }
    }

    private static Brush createFieldBorderBrush() {
        DrawingGroup group = new DrawingGroup();
        using (DrawingContext dc = group.Open()) {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 8, 8));
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 4, 4));
            dc.DrawRectangle(Brushes.Black, null, new Rect(4, 4, 4, 4));
        }
        DrawingBrush brush = new DrawingBrush(group) {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }
}
