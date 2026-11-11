using System.ComponentModel;
using System.Windows;
using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

/// <summary>
/// 0.9.0: „Letzten Bereich“ im Editor und Lupe im Editor verstellen (Zoom, Größe, Tastenhilfe).
/// </summary>
public class LastSelectionTests {
    private sealed class Host : IEditorHost {
        public PixelPoint GetCursorImagePoint() {
            return new PixelPoint(0, 0);
        }

        public string? AskSaveAsPath(string initialFolder, string fileName) {
            return null;
        }

        public void ShowError(string message) {
        }

        public void CloseEditor() {
        }

        public void DiscardCapture() {
        }
    }

    // zwei Monitore, der linke bei negativen Bildschirmkoordinaten
    private static readonly PixelRect virtualScreen = new PixelRect(-1920, 0, 3840, 1080);
    private static readonly PixelRect image = new PixelRect(0, 0, 3840, 1080);

    private static CaptureDocument createDocument(PixelRect? selection = null) {
        return new CaptureDocument {
            VirtualScreen = new PixelRect(-40, 0, 400, 300),
            Monitors = new List<MonitorInfo> { new MonitorInfo { Bounds = new PixelRect(0, 0, 400, 300), Primary = true } },
            Selection = selection
        };
    }

    [Fact]
    public void ToScreen_AndBack_UseVirtualScreenOrigin() {
        PixelRect selection = new PixelRect(2000, 100, 300, 200);
        PixelRect screen = LastSelectionRules.ToScreen(selection, virtualScreen);
        Assert.Equal(new PixelRect(80, 100, 300, 200), screen);
        Assert.Equal(selection, LastSelectionRules.ToImage(screen, virtualScreen, image));
        // linker Monitor: negative Bildschirmkoordinaten
        Assert.Equal(new PixelRect(-1820, 10, 50, 40), LastSelectionRules.ToScreen(new PixelRect(100, 10, 50, 40), virtualScreen));
    }

    [Fact]
    public void ToImage_ClampsPartially_AndRejectsOutside() {
        // ragt links aus dem virtuellen Bildschirm
        Assert.Equal(new PixelRect(0, 0, 80, 50), LastSelectionRules.ToImage(new PixelRect(-2000, -10, 160, 60), virtualScreen, image));
        // ragt rechts unten hinaus
        Assert.Equal(new PixelRect(3800, 1000, 40, 80), LastSelectionRules.ToImage(new PixelRect(1880, 1000, 100, 100), virtualScreen, image));
        // ganz außerhalb
        Assert.Null(LastSelectionRules.ToImage(new PixelRect(1920, 0, 100, 100), virtualScreen, image));
        Assert.Null(LastSelectionRules.ToImage(new PixelRect(0, 1080, 100, 100), virtualScreen, image));
    }

    [Fact]
    public void GetState_NamesTheReason() {
        PixelRect inside = new PixelRect(0, 0, 100, 100);
        Assert.Equal(LastSelectionState.None, LastSelectionRules.GetState(null, true, virtualScreen, image));
        Assert.Equal(LastSelectionState.None, LastSelectionRules.GetState(new PixelRect(0, 0, 0, 10), true, virtualScreen, image));
        Assert.Equal(LastSelectionState.OtherLayout, LastSelectionRules.GetState(inside, false, virtualScreen, image));
        Assert.Equal(LastSelectionState.OutsideImage, LastSelectionRules.GetState(new PixelRect(5000, 0, 10, 10), true, virtualScreen, image));
        Assert.Equal(LastSelectionState.Available, LastSelectionRules.GetState(inside, true, virtualScreen, image));
    }

    [Fact]
    public void ShouldRemember_OnlyChangedSelectionWithSameLayout() {
        PixelRect a = new PixelRect(1, 2, 30, 40);
        PixelRect b = new PixelRect(5, 6, 30, 40);
        Assert.True(LastSelectionRules.ShouldRemember(null, a, true, false));
        Assert.True(LastSelectionRules.ShouldRemember(a, b, true, false));
        // unverändert (z. B. alte Aufnahme nur angesehen), verworfen, andere Anordnung, kein Bereich
        Assert.False(LastSelectionRules.ShouldRemember(a, a, true, false));
        Assert.False(LastSelectionRules.ShouldRemember(null, a, true, true));
        Assert.False(LastSelectionRules.ShouldRemember(null, a, false, false));
        Assert.False(LastSelectionRules.ShouldRemember(a, null, true, false));
    }

    [Fact]
    public void Editor_ApplyLastSelection_IsOneUndoStep() {
        Sta.Run(() => {
            CaptureDocument document = createDocument();
            AppSettings settings = new AppSettings { LastSelection = new PixelRect(-20, 10, 100, 50) };
            EditorViewModel vm = new EditorViewModel(document, TestImages.CreatePattern(400, 300), new Host(), settings);
            // ohne passende Anordnung ausgegraut, Tooltip nennt den Grund
            Assert.False(vm.ApplyLastSelectionCommand.CanExecute(null));
            Assert.Equal(Strings.ToolbarLastSelectionOtherLayout, vm.LastSelectionToolTip);
            Assert.False(vm.ApplyLastSelection());

            List<string?> changed = new List<string?>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (s, e) => changed.Add(e.PropertyName);
            vm.IsSameLayout = true;
            Assert.Contains(nameof(EditorViewModel.LastSelectionToolTip), changed);
            Assert.True(vm.ApplyLastSelectionCommand.CanExecute(null));
            Assert.Equal(Strings.ToolbarLastSelection, vm.LastSelectionToolTip);

            vm.ApplyLastSelectionCommand.Execute(null);
            Assert.Equal(new PixelRect(20, 10, 100, 50), document.Selection);
            Assert.Equal(1, vm.UndoStack.UndoCount);
            // derselbe Befehl wie beim Aufziehen mit der Maus
            Assert.Equal(Strings.UndoSelection, vm.UndoStack.UndoDescription);
            // nochmal: schon gesetzt, kein weiterer Schritt
            Assert.False(vm.ApplyLastSelection());
            Assert.Equal(1, vm.UndoStack.UndoCount);

            Assert.True(vm.Undo());
            Assert.Null(document.Selection);
            Assert.True(vm.Redo());
            Assert.Equal(new PixelRect(20, 10, 100, 50), document.Selection);
        });
    }

    [Fact]
    public void Editor_ApplyLastSelection_ClampsToImage() {
        Sta.Run(() => {
            CaptureDocument document = createDocument(new PixelRect(1, 1, 5, 5));
            AppSettings settings = new AppSettings { LastSelection = new PixelRect(300, 250, 200, 200) };
            EditorViewModel vm = new EditorViewModel(document, TestImages.CreatePattern(400, 300), new Host(), settings) { IsSameLayout = true };
            Assert.True(vm.ApplyLastSelection());
            Assert.Equal(new PixelRect(340, 250, 60, 50), document.Selection);
            Assert.True(vm.Undo());
            Assert.Equal(new PixelRect(1, 1, 5, 5), document.Selection);

            vm.LastSelection = new PixelRect(1000, 1000, 10, 10);
            Assert.Equal(LastSelectionState.OutsideImage, vm.LastSelectionState);
            Assert.Equal(Strings.ToolbarLastSelectionOutside, vm.LastSelectionToolTip);
            Assert.False(vm.ApplyLastSelectionCommand.CanExecute(null));
            vm.LastSelection = null;
            Assert.Equal(Strings.ToolbarLastSelectionNone, vm.LastSelectionToolTip);
        });
    }

    [Fact]
    public void Editor_GetSelectionToRemember_InScreenCoordinates() {
        Sta.Run(() => {
            CaptureDocument document = createDocument();
            EditorViewModel vm = new EditorViewModel(document, TestImages.CreatePattern(400, 300), new Host(), new AppSettings()) { IsSameLayout = true };
            Assert.Null(vm.GetSelectionToRemember(false));
            vm.SetSelection(new PixelRect(50, 60, 70, 80));
            Assert.Equal(new PixelRect(10, 60, 70, 80), vm.GetSelectionToRemember(false));
            Assert.Null(vm.GetSelectionToRemember(true));
            vm.IsSameLayout = false;
            Assert.Null(vm.GetSelectionToRemember(false));

            // vorhandener, unveränderter Bereich (alte Aufnahme nur angesehen) wird nicht gemerkt
            CaptureDocument old = createDocument(new PixelRect(5, 5, 20, 20));
            EditorViewModel viewer = new EditorViewModel(old, TestImages.CreatePattern(400, 300), new Host(), new AppSettings()) { IsSameLayout = true };
            Assert.Null(viewer.GetSelectionToRemember(false));
        });
    }

    [Fact]
    public void Settings_LastSelection_NormalizeAndRoundTrip() {
        AppSettings settings = new AppSettings { LastSelection = new PixelRect(10, 10, 0, 50) };
        settings.Normalize();
        Assert.Null(settings.LastSelection);
        settings.LastSelection = new PixelRect(10, 10, 200000, 50);
        settings.Normalize();
        Assert.Null(settings.LastSelection);
        settings.LastSelection = new PixelRect(-1800, 20, 300, 200);
        settings.Normalize();
        Assert.Equal(new PixelRect(-1800, 20, 300, 200), settings.LastSelection);

        using (TempFolder temp = new TempFolder()) {
            string file = Path.Combine(temp.Path, "settings.json");
            SettingsService service = new SettingsService(file);
            service.Load();
            Assert.Null(service.Settings.LastSelection);
            Assert.True(service.Settings.ShowMagnifierKeyHints);
            service.Settings.LastSelection = new PixelRect(-1800, 20, 300, 200);
            service.Settings.ShowMagnifierKeyHints = false;
            service.Save();
            string json = File.ReadAllText(file);
            Assert.Contains("\"lastSelection\"", json);
            Assert.Contains("\"showMagnifierKeyHints\": false", json);

            SettingsService reloaded = new SettingsService(file);
            reloaded.Load();
            Assert.Equal(new PixelRect(-1800, 20, 300, 200), reloaded.Settings.LastSelection);
            Assert.False(reloaded.Settings.ShowMagnifierKeyHints);

            // ungültiger Bereich in der Datei → keiner
            File.WriteAllText(file, "{ \"settingsVersion\": 1, \"lastSelection\": { \"x\": 1, \"y\": 2, \"width\": -5, \"height\": 3 } }");
            SettingsService broken = new SettingsService(file);
            broken.Load();
            Assert.Null(broken.Settings.LastSelection);
            Assert.True(broken.Settings.ShowMagnifierKeyHints);
        }
    }
}

public class MagnifierInEditorTests {
    private sealed class FakeAutostart : IAutostartService {
        public bool IsEnabled { get; set; }

        public void SetEnabled(bool enabled) {
            IsEnabled = enabled;
        }
    }

    [Theory]
    [InlineData(8, 1, 9)]
    [InlineData(8, -1, 7)]
    [InlineData(16, 1, 16)]
    [InlineData(4, -1, 4)]
    [InlineData(8, 120, 9)]
    [InlineData(8, -240, 7)]
    [InlineData(30, -1, 15)]
    [InlineData(1, 1, 5)]
    public void NextZoom_WholeStepsWithinRange(int current, int direction, int expected) {
        Assert.Equal(expected, MagnifierSteps.NextZoom(current, direction));
    }

    [Theory]
    [InlineData(160, 1, 170)]
    [InlineData(160, -1, 150)]
    [InlineData(400, 1, 400)]
    [InlineData(80, -1, 80)]
    [InlineData(165, 1, 170)]
    [InlineData(165, -1, 160)]
    [InlineData(64, 1, 80)]
    [InlineData(64, -1, 80)]
    [InlineData(600, -1, 400)]
    [InlineData(600, 1, 400)]
    [InlineData(395, 1, 400)]
    public void NextSize_TenStepsWithinSliderRange(int current, int direction, int expected) {
        Assert.Equal(expected, MagnifierSteps.NextSize(current, direction));
    }

    [Fact]
    public void SizeRange_MatchesSettingsSlider() {
        Assert.Equal(80, SettingsViewModel.MagnifierSizeMin);
        Assert.Equal(400, SettingsViewModel.MagnifierSizeMax);
    }

    [Fact]
    public void KeyHints_NameBindingAndValues() {
        Assert.Equal("Rad: Zoom 8×   +/-: Größe 160 px", MagnifierSteps.FormatKeyHints(false, 8, 160));
        Assert.Equal("Alt+Rad: Zoom 12×   +/-: Größe 240 px", MagnifierSteps.FormatKeyHints(true, 12, 240));
    }

    [Fact]
    public void Magnifier_HintLine_IsPartOfSize_AndPlacementStaysInside() {
        Sta.Run(() => {
            Views.Magnifier magnifier = new Views.Magnifier();
            magnifier.Configure(TestImages.CreatePattern(200, 150), 8, 160, 1);
            magnifier.Show(new PixelPoint(50, 50), null);
            magnifier.UpdateLayout();
            magnifier.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size without = magnifier.DesiredSize;
            Assert.Equal("", magnifier.KeyHints);

            string hints = MagnifierSteps.FormatKeyHints(false, 8, 160);
            magnifier.SetKeyHints(hints);
            Assert.Equal(hints, magnifier.KeyHints);
            magnifier.UpdateLayout();
            magnifier.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size with = magnifier.DesiredSize;
            Assert.True(with.Height > without.Height + 5, $"Höhe mit Hilfezeile {with.Height}, ohne {without.Height}");

            // Cursor nahe der unteren Kante: die höhere Lupe muss nach oben ausweichen und ganz im Monitor bleiben
            Rect monitor = new Rect(0, 0, 1920, 1080);
            Point cursor = new Point(500, 1080 - without.Height - 30);
            Point position = MagnifierPlacement.Place(cursor, with, monitor, 26);
            Assert.True(position.Y + with.Height <= monitor.Bottom);
            Assert.True(position.Y < cursor.Y);

            // größere Lupe: wieder innerhalb
            magnifier.Configure(TestImages.CreatePattern(200, 150), 8, 400, 1);
            magnifier.UpdateLayout();
            magnifier.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size larger = magnifier.DesiredSize;
            Assert.True(larger.Height > with.Height);
            Point corner = MagnifierPlacement.Place(new Point(1900, 1070), larger, monitor, 26);
            Assert.True(corner.X >= 0 && corner.Y >= 0 && corner.X + larger.Width <= monitor.Right && corner.Y + larger.Height <= monitor.Bottom);

            magnifier.SetKeyHints(null);
            Assert.Equal("", magnifier.KeyHints);
        });
    }

    [Fact]
    public void SettingsViewModel_PicksUpMagnifierValuesSavedElsewhere() {
        using (TempFolder temp = new TempFolder()) {
            SettingsService service = new SettingsService(Path.Combine(temp.Path, "settings.json"));
            service.Load();
            SettingsViewModel vm = new SettingsViewModel(service, new FakeAutostart(), null, () => false);
            Assert.True(vm.ShowMagnifierKeyHints);
            Assert.Equal(8, vm.MagnifierZoom);
            Assert.Equal(160, vm.MagnifierSize);

            // Benutzer verschiebt einen Regler im offenen Fenster; ein anderes Speichern ohne Lupen-Änderung lässt das stehen
            vm.MagnifierSize = 200;
            service.Settings.DefaultThickness = 6;
            service.Save();
            Assert.Equal(200, vm.MagnifierSize);

            // Editor verstellt die Lupe und speichert: das Fenster zieht nach
            service.Settings.MagnifierZoom = 11;
            service.Settings.MagnifierSize = 250;
            service.Save();
            Assert.Equal(11, vm.MagnifierZoom);
            Assert.Equal(250, vm.MagnifierSize);

            // Übernehmen schreibt diese Werte (nicht die alten) und die Tastenhilfe
            vm.ShowMagnifierKeyHints = false;
            Assert.True(vm.Apply());
            Assert.Equal(11, service.Settings.MagnifierZoom);
            Assert.Equal(250, service.Settings.MagnifierSize);
            Assert.False(service.Settings.ShowMagnifierKeyHints);

            // nach dem Lösen keine Reaktion mehr
            vm.Detach();
            service.Settings.MagnifierZoom = 5;
            service.Save();
            Assert.Equal(11, vm.MagnifierZoom);
        }
    }

    [Fact]
    public void MagnifierAndToolbar_XamlLoads() {
        Sta.Run(() => {
            Views.Magnifier magnifier = new Views.Magnifier();
            Assert.NotNull(magnifier.Content);
            Views.EditorToolbar toolbar = new Views.EditorToolbar();
            Assert.NotNull(toolbar.Content);
        });
    }
}
