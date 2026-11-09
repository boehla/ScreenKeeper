using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.Views;

/// <summary>
/// Inline-Textbearbeitung: ein Textfeld im Overlay genau über der Stelle des Textes, mit Schrift, Größe (gezoomt) und
/// Farbe des Objekts. Enter übernimmt, Shift+Enter fügt eine Zeile ein, Esc verwirft; ein Klick daneben übernimmt.
/// </summary>
public partial class EditorWindow {
    // Innenabstand des TextBox-Inhalts (Rahmen 1 + Textansicht 2 DIP), damit der Text an derselben Stelle steht wie gezeichnet
    private const double textBoxInsetX = 3;
    private const double textBoxInsetY = 1;

    private bool updatingTextEditor;
    private string? textEditorAnnotationId;

    private void attachTextEditor() {
        viewModel.TextEditChanged += (s, e) => updateTextEditor();
        textEditor.PreviewKeyDown += onTextEditorPreviewKeyDown;
        textEditor.TextChanged += (s, e) => {
            if(!updatingTextEditor) viewModel.UpdateEditingText(textEditor.Text);
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
    }

    /// <summary>
    /// Position und Größe des Textfelds an Zoom/Pan anpassen (auch während der Bearbeitung).
    /// </summary>
    private void updateTextEditorLayout() {
        TextAnnotation? text = viewModel.EditingText;
        if(text == null || textEditor.Visibility != Visibility.Visible) return;
        double zoom = transform.Zoom;
        double padding = TextLayout.GetPadding(text.FontSize) * zoom;
        double fontSize = Math.Max(1, text.FontSize * zoom);
        textEditor.FontSize = fontSize;
        textEditor.Padding = new Thickness(padding, padding, padding, padding);
        textEditor.MinWidth = fontSize + 2 * padding + 2 * textBoxInsetX;
        textEditor.Width = text.Width.HasValue ? text.Width.Value * zoom + 2 * padding + 2 * textBoxInsetX : double.NaN;
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
        if(e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) {
            e.Handled = true;
            viewModel.UpdateEditingText(textEditor.Text);
            viewModel.CommitTextEdit();
        }
    }
}
