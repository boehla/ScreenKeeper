using System.Windows;
using System.Windows.Input;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.Services.Native;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Tools;

namespace ScreenKeeper.Views;

/// <summary>
/// Eingabe-Teil des Editors: Maus → Bildpixel → aktives Werkzeug, Zoom/Pan im Fenster-Modus, Tastatur.
/// </summary>
public partial class EditorWindow {
    private const double wheelZoomStep = 1.25;
    private const double wheelPanStep = 0.5;
    private const double autoPanMargin = 24;

    private bool pointerCaptured;
    private bool isPanning;
    private MouseButton panButton;
    private Point panStartMouse;
    private double panStartX;
    private double panStartY;
    private bool spaceHeld;
    private bool spaceUsedForPan;
    // Position (Ansichts-DIPs), auf die die Pfeiltasten den Mauszeiger gesetzt haben; das daraus folgende
    // Mausereignis wird ignoriert, weil der Pixel schon verarbeitet ist (bei Zoom < 100 % träfe es sonst einen Nachbarpixel)
    private Point? keyboardCursorView;

    private void attachInput() {
        viewport.MouseLeftButtonDown += onViewportLeftButtonDown;
        viewport.MouseLeftButtonUp += onViewportLeftButtonUp;
        viewport.MouseDown += onViewportMouseDown;
        viewport.MouseUp += onViewportMouseUp;
        viewport.MouseMove += onViewportMouseMove;
        viewport.MouseWheel += onViewportMouseWheel;
        viewport.MouseLeave += (s, e) => updateMagnifier();
        viewport.LostMouseCapture += onViewportLostMouseCapture;
        PreviewKeyDown += onPreviewKeyDown;
        PreviewKeyUp += onPreviewKeyUp;
    }

    /// <summary>
    /// Die zentrale Umrechnung Ansicht → Bild (Bildpixel = floor((Maus − Pan) / Zoom)).
    /// </summary>
    private PointerInput toInput(Point view) {
        Point image = transform.ToImage(view);
        return PointerInput.FromImage(image.X, image.Y, Keyboard.Modifiers);
    }

    private void syncPointerFromMouse() {
        PointerInput input = toInput(getMouseInViewport());
        viewModel.PointerMove(input);
        updateCursor(input);
    }

    private void onViewportLeftButtonDown(object sender, MouseButtonEventArgs e) {
        e.Handled = true;
        Point view = e.GetPosition(viewport);
        if(isPanning) return;
        if(mode == EditorMode.Windowed && spaceHeld) {
            spaceUsedForPan = true;
            startPan(view, MouseButton.Left);
            return;
        }
        keyboardCursorView = null;
        pointerCaptured = true;
        viewport.CaptureMouse();
        PointerInput input = toInput(view) with { ClickCount = e.ClickCount };
        viewModel.PointerDown(input);
        updateCursor(input);
    }

    private void onViewportLeftButtonUp(object sender, MouseButtonEventArgs e) {
        e.Handled = true;
        if(isPanning) {
            if(panButton == MouseButton.Left) endPan();
            return;
        }
        if(!pointerCaptured) return;
        PointerInput input = toInput(e.GetPosition(viewport));
        pointerCaptured = false;
        viewModel.PointerUp(input);
        viewport.ReleaseMouseCapture();
        updateCursor(input);
    }

    private void onViewportMouseDown(object sender, MouseButtonEventArgs e) {
        if(e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        if(mode == EditorMode.Windowed && !pointerCaptured && !isPanning) startPan(e.GetPosition(viewport), MouseButton.Middle);
    }

    private void onViewportMouseUp(object sender, MouseButtonEventArgs e) {
        if(e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        if(isPanning && panButton == MouseButton.Middle) endPan();
    }

    private void onViewportMouseMove(object sender, MouseEventArgs e) {
        Point view = e.GetPosition(viewport);
        if(isPanning) {
            transform.PanX = panStartX + view.X - panStartMouse.X;
            transform.PanY = panStartY + view.Y - panStartMouse.Y;
            applyTransform();
            return;
        }
        if(keyboardCursorView.HasValue) {
            Vector distance = view - keyboardCursorView.Value;
            if(Math.Abs(distance.X) * deviceScale < 1.5 && Math.Abs(distance.Y) * deviceScale < 1.5) return;
            keyboardCursorView = null;
        }
        PointerInput input = toInput(view);
        viewModel.PointerMove(input);
        updateCursor(input);
        updateMagnifier();
    }

    private void onViewportMouseWheel(object sender, MouseWheelEventArgs e) {
        ModifierKeys modifiers = Keyboard.Modifiers;
        if(mode != EditorMode.Windowed) {
            // Vollbild: das Rad ist sonst frei → Lupen-Zoom (ohne Zusatztaste, Alt+Rad wie im Fenstermodus ebenfalls)
            if(modifiers != ModifierKeys.None && modifiers != ModifierKeys.Alt) return;
            if(!isMagnifierShown || e.Delta == 0) return;
            e.Handled = true;
            if(modifiers == ModifierKeys.Alt) altWheelUsed = true;
            changeMagnifierZoom(Math.Sign(e.Delta));
            return;
        }
        e.Handled = true;
        Point view = e.GetPosition(viewport);
        if((modifiers & ModifierKeys.Alt) != 0) {
            // Fenstermodus: Rad scrollt bzw. zoomt die Ansicht, Alt+Rad ändert den Lupen-Zoom. Das Loslassen von Alt darf
            // danach nicht das Fenstermenü aktivieren (siehe onPreviewKeyUp).
            altWheelUsed = true;
            if(isMagnifierShown && e.Delta != 0) changeMagnifierZoom(Math.Sign(e.Delta));
            return;
        }
        if((modifiers & ModifierKeys.Control) != 0) {
            double zoom = Math.Clamp(transform.Zoom * Math.Pow(wheelZoomStep, e.Delta / 120.0), minZoom, maxZoom);
            transform.ZoomAt(view, zoom);
        } else if((modifiers & ModifierKeys.Shift) != 0) {
            transform.PanX += e.Delta * wheelPanStep;
        } else {
            transform.PanY += e.Delta * wheelPanStep;
        }
        applyTransform();
        if(!isPanning) {
            PointerInput input = toInput(view);
            viewModel.PointerMove(input);
            updateCursor(input);
        }
    }

    private void onViewportLostMouseCapture(object sender, MouseEventArgs e) {
        if(isPanning) {
            isPanning = false;
            updateCursorAtMouse();
        }
        if(pointerCaptured) {
            // Mausfang unerwartet verloren (z. B. Alt+Tab): Ziehvorgang verwerfen
            pointerCaptured = false;
            viewModel.CancelGesture();
        }
    }

    private void startPan(Point view, MouseButton button) {
        isPanning = true;
        panButton = button;
        panStartMouse = view;
        panStartX = transform.PanX;
        panStartY = transform.PanY;
        viewport.CaptureMouse();
        viewport.Cursor = Cursors.ScrollAll;
        updateMagnifier();
    }

    private void endPan() {
        isPanning = false;
        viewport.ReleaseMouseCapture();
        updateCursorAtMouse();
        updateMagnifier();
    }

    private void onPreviewKeyDown(object sender, KeyEventArgs e) {
        // Während der Textbearbeitung gehören alle Tasten dem Textfeld (Enter/Esc behandelt es selbst)
        if(viewModel.IsTextEditing) return;
        // ebenso während der Eingabe einer eigenen Schriftgröße in der Werkzeugleiste
        if(Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Alt erneut gedrückt (ohne Rad): normales Verhalten beim Loslassen
        if((key == Key.LeftAlt || key == Key.RightAlt) && !e.IsRepeat) altWheelUsed = false;
        ModifierKeys modifiers = Keyboard.Modifiers;
        // Alt-Kombinationen (Alt+F4 …) bleiben beim System
        if((modifiers & ModifierKeys.Alt) != 0) return;
        bool control = (modifiers & ModifierKeys.Control) != 0;
        bool shift = (modifiers & ModifierKeys.Shift) != 0;
        if(!control && !shift) {
            string? toolKey = ToolShortcuts.GetToolKey(key);
            if(toolKey != null) {
                e.Handled = true;
                if(e.IsRepeat || pointerCaptured || isPanning) return;
                viewModel.SelectToolByKey(toolKey);
                updateCursorAtMouse();
                return;
            }
        }
        switch(key) {
            case Key.Escape:
                e.Handled = true;
                if(isPanning) {
                    endPan();
                } else if(viewModel.IsGestureActive) {
                    cancelGesture();
                } else if(viewModel.HasSelectedAnnotation && viewModel.IsSelectingToolActive) {
                    viewModel.SelectAnnotation(null);
                    updateCursorAtMouse();
                } else {
                    Close();
                }
                return;
            case Key.Delete:
            case Key.Back:
                if(!viewModel.HasSelectedAnnotation) return;
                e.Handled = true;
                if(pointerCaptured) return;
                viewModel.DeleteSelectedAnnotation();
                updateCursorAtMouse();
                return;
            case Key.D:
                if(!control) return;
                e.Handled = true;
                if(pointerCaptured) return;
                viewModel.DuplicateSelectedAnnotation();
                return;
            case Key.Z:
                if(!control) return;
                e.Handled = true;
                execute(shift ? viewModel.RedoCommand : viewModel.UndoCommand);
                return;
            case Key.Y:
                if(!control) return;
                e.Handled = true;
                execute(viewModel.RedoCommand);
                return;
            case Key.L:
                // Strg+L: letzten Bereich übernehmen (L allein wählt das Linien-Werkzeug, siehe oben)
                if(!control) return;
                e.Handled = true;
                if(e.IsRepeat || pointerCaptured || isPanning) return;
                execute(viewModel.ApplyLastSelectionCommand);
                updateCursorAtMouse();
                return;
            case Key.OemPlus:
            case Key.Add:
            case Key.OemMinus:
            case Key.Subtract:
                // +/-: Lupengröße, nur solange die Lupe zu sehen ist
                if(control || !isMagnifierShown) return;
                e.Handled = true;
                changeMagnifierSize(key == Key.OemPlus || key == Key.Add ? 1 : -1);
                return;
            case Key.C:
                if(!control) return;
                e.Handled = true;
                execute(shift ? viewModel.CopyFullCommand : viewModel.CopySelectionCommand);
                return;
            case Key.S:
                if(!control) return;
                e.Handled = true;
                execute(shift ? viewModel.SaveSelectionAsCommand : viewModel.SaveSelectionCommand);
                return;
            case Key.Enter:
                e.Handled = true;
                if(e.IsRepeat || pointerCaptured) return;
                if(viewModel.IsKeyboardSelecting) {
                    viewModel.KeyboardMark();
                } else if(viewModel.HasSelection && !viewModel.IsGestureActive) {
                    execute(viewModel.CopySelectionCommand);
                } else {
                    viewModel.KeyboardMark();
                }
                return;
            case Key.Space:
                e.Handled = true;
                if(e.IsRepeat || pointerCaptured) return;
                if(mode == EditorMode.Windowed && !viewModel.IsKeyboardSelecting) {
                    // Fenster-Modus: Leertaste + Ziehen verschiebt; ohne Ziehen setzt das Loslassen den Startpunkt
                    spaceHeld = true;
                    spaceUsedForPan = false;
                    updateCursorAtMouse();
                } else {
                    viewModel.KeyboardMark();
                }
                return;
            case Key.Left:
                e.Handled = true;
                moveCursorBy(-1, 0, shift);
                return;
            case Key.Right:
                e.Handled = true;
                moveCursorBy(1, 0, shift);
                return;
            case Key.Up:
                e.Handled = true;
                moveCursorBy(0, -1, shift);
                return;
            case Key.Down:
                e.Handled = true;
                moveCursorBy(0, 1, shift);
                return;
        }
    }

    private void onPreviewKeyUp(object sender, KeyEventArgs e) {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if(altWheelUsed && (key == Key.LeftAlt || key == Key.RightAlt)) {
            // Alt wurde für Alt+Rad benutzt: das Loslassen nicht an Windows weitergeben (würde das Fenstermenü aktivieren)
            altWheelUsed = false;
            e.Handled = true;
            return;
        }
        if(e.Key != Key.Space || !spaceHeld) return;
        e.Handled = true;
        spaceHeld = false;
        if(isPanning && panButton == MouseButton.Left) {
            endPan();
        } else if(!spaceUsedForPan && !pointerCaptured) {
            viewModel.KeyboardMark();
        }
        updateCursorAtMouse();
    }

    private void cancelGesture() {
        viewModel.CancelGesture();
        if(pointerCaptured) {
            pointerCaptured = false;
            viewport.ReleaseMouseCapture();
        }
        updateCursorAtMouse();
    }

    private static void execute(ICommand command) {
        if(command.CanExecute(null)) command.Execute(null);
    }

    /// <summary>
    /// Pfeiltasten: Cursor um genau 1 Bildpixel (Shift: 10) bewegen und den echten Mauszeiger mitführen.
    /// Während eines Ziehvorgangs bewegt das die aktive Kante bzw. den aktiven Anfasser.
    /// </summary>
    private void moveCursorBy(int dx, int dy, bool large) {
        if(isPanning) return;
        PixelPoint target = SelectionGeometry.MoveCursor(viewModel.CursorPixel, dx, dy, large, viewModel.ImageBounds);
        if(mode == EditorMode.Windowed) ensureVisible(target);
        Point view = transform.ToView(target.X + 0.5, target.Y + 0.5);
        keyboardCursorView = view;
        setRealCursor(target, view);
        PointerInput input = PointerInput.AtPixel(target, Keyboard.Modifiers);
        viewModel.PointerMove(input);
        updateCursor(input);
        updateMagnifier();
    }

    /// <summary>
    /// Echten Mauszeiger auf einen Bildpixel setzen: im Vollbild Bildschirm = Bildpixel + Ursprung des virtuellen
    /// Bildschirms, im Fenster-Modus über die Transformation.
    /// </summary>
    private void setRealCursor(PixelPoint pixel, Point view) {
        if(mode == EditorMode.Fullscreen) {
            PixelPoint screen = ScreenCoordinates.ImageToScreen(pixel, document.VirtualScreen);
            User32.SetCursorPos(screen.X, screen.Y);
            return;
        }
        Point screenPoint = viewport.PointToScreen(view);
        User32.SetCursorPos((int)Math.Floor(screenPoint.X), (int)Math.Floor(screenPoint.Y));
    }

    /// <summary>
    /// Fenster-Modus: Ansicht verschieben, wenn der Pixel (fast) aus dem sichtbaren Bereich läuft.
    /// </summary>
    private void ensureVisible(PixelPoint pixel) {
        Point view = transform.ToView(pixel.X + 0.5, pixel.Y + 0.5);
        double width = viewport.ActualWidth;
        double height = viewport.ActualHeight;
        double shiftX = 0;
        double shiftY = 0;
        if(view.X < autoPanMargin) shiftX = autoPanMargin - view.X;
        if(view.X > width - autoPanMargin) shiftX = width - autoPanMargin - view.X;
        if(view.Y < autoPanMargin) shiftY = autoPanMargin - view.Y;
        if(view.Y > height - autoPanMargin) shiftY = height - autoPanMargin - view.Y;
        if(shiftX == 0 && shiftY == 0) return;
        transform.PanX += Math.Round(shiftX);
        transform.PanY += Math.Round(shiftY);
        applyTransform();
    }

    private void updateCursorAtMouse() {
        updateCursor(toInput(getMouseInViewport()));
    }

    private void updateCursor(PointerInput input) {
        if(isPanning) {
            viewport.Cursor = Cursors.ScrollAll;
            return;
        }
        if(spaceHeld && mode == EditorMode.Windowed) {
            viewport.Cursor = Cursors.Hand;
            return;
        }
        viewport.Cursor = toWpfCursor(viewModel.GetCursor(input));
    }

    private static Cursor toWpfCursor(EditorCursor cursor) {
        switch(cursor) {
            case EditorCursor.Arrow: return Cursors.Arrow;
            case EditorCursor.SizeAll: return Cursors.SizeAll;
            case EditorCursor.SizeNS: return Cursors.SizeNS;
            case EditorCursor.SizeWE: return Cursors.SizeWE;
            case EditorCursor.SizeNWSE: return Cursors.SizeNWSE;
            case EditorCursor.SizeNESW: return Cursors.SizeNESW;
            case EditorCursor.Hand: return Cursors.Hand;
            case EditorCursor.IBeam: return Cursors.IBeam;
        }
        return Cursors.Cross;
    }
}
