using System.Reflection;
using System.Text.Json;
using System.Windows.Input;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

public class HotkeyConflictTests {
    private static List<(HotkeyAction Action, string Text)> bindings(string edit, string save, string history) {
        return new List<(HotkeyAction Action, string Text)> {
            (HotkeyAction.CaptureEdit, edit),
            (HotkeyAction.CaptureSave, save),
            (HotkeyAction.ShowHistory, history)
        };
    }

    [Fact]
    public void Defaults_HaveNoIssues() {
        Assert.Empty(HotkeyConflictChecker.Check(bindings("PrintScreen", "Ctrl+PrintScreen", "Shift+PrintScreen"), null, false));
    }

    [Fact]
    public void SameBinding_IsError_EvenWithDifferentSpelling() {
        List<HotkeyIssue> issues = HotkeyConflictChecker.Check(bindings("Ctrl+PrintScreen", "Strg+Druck", ""), null, false);
        HotkeyIssue issue = Assert.Single(issues);
        Assert.Equal(HotkeyIssueSeverity.Error, issue.Severity);
        Assert.Contains("denselben Hotkey", issue.Message);
    }

    [Fact]
    public void EmptyBindings_AreNoConflict() {
        Assert.Empty(HotkeyConflictChecker.Check(bindings("", "", "Ctrl+Alt+H"), null, false));
    }

    [Fact]
    public void InvalidAndUnmodifiedLetters_AreErrors() {
        List<HotkeyIssue> issues = HotkeyConflictChecker.Check(bindings("Ctrl+Quatsch", "H", "Shift+A"), null, false);
        Assert.Equal(3, issues.Count);
        Assert.All(issues, i => Assert.Equal(HotkeyIssueSeverity.Error, i.Severity));
        // F-Tasten und Druck sind ohne Modifizierer erlaubt
        Assert.Empty(HotkeyConflictChecker.Check(bindings("F9", "PrintScreen", "Ctrl+Shift+H"), null, false));
    }

    [Fact]
    public void TakenByOtherProgram_IsWarning() {
        List<HotkeyIssue> issues = HotkeyConflictChecker.Check(bindings("PrintScreen", "Ctrl+Alt+S", "Ctrl+Alt+H"),
            (action, binding) => action == HotkeyAction.CaptureSave ? (HotkeyProbeResult.Available, 0) : (HotkeyProbeResult.Taken, HotkeyService.ErrorHotkeyAlreadyRegistered),
            false);
        Assert.Equal(2, issues.Count);
        Assert.All(issues, i => Assert.Equal(HotkeyIssueSeverity.Warning, i.Severity));
        Assert.Contains("Tastatur-Hook", issues.Single(i => i.Action == HotkeyAction.CaptureEdit).Message);
        Assert.Contains("anderen Programm", issues.Single(i => i.Action == HotkeyAction.ShowHistory).Message);
    }

    [Fact]
    public void SnippingEnabled_WarnsOnlyForPlainPrintScreen() {
        List<HotkeyIssue> issues = HotkeyConflictChecker.Check(bindings("PrintScreen", "Ctrl+PrintScreen", ""), null, true);
        HotkeyIssue issue = Assert.Single(issues);
        Assert.Equal(HotkeyAction.CaptureEdit, issue.Action);
        Assert.Equal(HotkeyIssueSeverity.Warning, issue.Severity);
    }

    [Fact]
    public void HotkeyService_StatusProbeAndSuspend() {
        Sta.Run(() => {
            using (TempFolder temp = new TempFolder()) {
                AppSettings settings = new AppSettings {
                    HotkeyCaptureEdit = "Ctrl+Alt+Shift+F23",
                    HotkeyCaptureSave = "",
                    HotkeyHistory = "Quatsch+X"
                };
                using (HotkeyService first = new HotkeyService()) {
                    using (HotkeyService second = new HotkeyService()) {
                        first.Apply(settings);
                        Assert.Equal(HotkeyState.Registered, first.Status[HotkeyAction.CaptureEdit].State);
                        Assert.Equal(HotkeyState.Off, first.Status[HotkeyAction.CaptureSave].State);
                        Assert.Equal(HotkeyState.Invalid, first.Status[HotkeyAction.ShowHistory].State);

                        HotkeyBinding binding = HotkeyBinding.Parse(settings.HotkeyCaptureEdit);
                        // eigene Registrierung gilt als frei, für eine andere Anwendung ist sie belegt
                        Assert.Equal(HotkeyProbeResult.Available, first.Probe(binding, out _));
                        Assert.Equal(HotkeyProbeResult.Taken, second.Probe(binding, out int error));
                        Assert.Equal(HotkeyService.ErrorHotkeyAlreadyRegistered, error);

                        first.Suspend();
                        Assert.Equal(HotkeyState.Suspended, first.Status[HotkeyAction.CaptureEdit].State);
                        Assert.Equal(HotkeyProbeResult.Available, second.Probe(binding, out _));
                        first.Resume();
                        Assert.Equal(HotkeyState.Registered, first.Status[HotkeyAction.CaptureEdit].State);
                        Assert.Equal(HotkeyProbeResult.Taken, second.Probe(binding, out _));
                    }
                }
            }
        });
    }

    [Fact]
    public void FromKey_AndDisplay() {
        HotkeyBinding? binding = HotkeyBinding.FromKey(Key.Snapshot, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.NotNull(binding);
        Assert.Equal("Ctrl+Shift+PrintScreen", binding!.ToString());
        Assert.Equal("Strg+Umschalt+Druck", binding.ToDisplayString());
        Assert.Null(HotkeyBinding.FromKey(Key.LeftCtrl, ModifierKeys.Control));
        Assert.Equal("Alt+F5", HotkeyBinding.FromKey(Key.F5, ModifierKeys.Alt)!.ToString());
        Assert.Equal("Ctrl+Win+K", HotkeyBinding.FromKey(Key.K, ModifierKeys.Control | ModifierKeys.Windows)!.ToString());
        Assert.Equal("", HotkeyBinding.ToDisplayString(""));
        Assert.True(HotkeyBinding.Parse("Shift+K").NeedsModifier);
        Assert.False(HotkeyBinding.Parse("Ctrl+K").NeedsModifier);
        Assert.False(HotkeyBinding.Parse("F12").NeedsModifier);
    }
}

public class SettingsPhase5Tests {
    private sealed class FakeAutostart : IAutostartService {
        public bool IsEnabled { get; set; }

        public int Calls { get; private set; }

        public void SetEnabled(bool enabled) {
            IsEnabled = enabled;
            Calls++;
        }
    }

    [Fact]
    public void Roundtrip_AllFields() {
        using (TempFolder temp = new TempFolder()) {
            string path = Path.Combine(temp.Path, "settings.json");
            SettingsService service = new SettingsService(path);
            service.Load();
            AppSettings s = service.Settings;
            s.StorageFolder = @"D:\Ablage";
            s.FileNamePattern = "Bild {yyyy}{MM}{dd}-{HH}{mm}{ss}";
            s.ExportFolder = @"%USERPROFILE%\Export";
            s.HotkeyCaptureEdit = "Ctrl+Alt+PrintScreen";
            s.HotkeyCaptureSave = "Alt+PrintScreen";
            s.HotkeyHistory = "";
            s.HotkeyCaptureDelayed = "Ctrl+Alt+D";
            s.HotkeyRemotePrintScreen = "Ctrl+Shift+PrintScreen";
            s.CaptureHotkeysInRemoteSessions = false;
            s.RemoteSessionWindowClasses = "TscShellContainerClass;VMwareRemote";
            s.CaptureDelaySeconds = 10;
            s.CaptureDelayAskEachTime = true;
            s.DelayedCaptureAction = DelayedCaptureActions.Save;
            s.MagnifierZoom = 12;
            s.MagnifierSize = 240;
            s.DefaultColor = "#FF1E88E5";
            s.HighlightColor = "#FF43A047";
            s.DefaultThickness = 6;
            s.DefaultFontSize = 32;
            s.DefaultTextAutoFontSize = false;
            s.DefaultTextAlignment = TextAlignments.BottomCenter;
            s.DefaultBlockSize = 14;
            s.DefaultBlurRadius = 12;
            s.DefaultFillColor = "#801E88E5";
            s.DefaultLineStartCap = LineCaps.Dot;
            s.DefaultLineEndCap = LineCaps.Square;
            s.DefaultArrowStartCap = LineCaps.Triangle;
            s.DefaultArrowEndCap = LineCaps.None;
            s.DefaultFreehandStartCap = LineCaps.Square;
            s.DefaultFreehandEndCap = LineCaps.Arrow;
            s.DefaultTextBackground = true;
            s.DefaultSmooth = false;
            s.DefaultStampSize = 48;
            s.DefaultCursorKind = CursorKinds.Hand;
            s.CursorVisible = false;
            s.Autostart = true;
            s.RetentionDays = 90;
            s.CheckForUpdates = false;
            s.NotifyOnSaveOnly = false;
            s.HistoryWindow = new WindowPlacement { Left = -1200, Top = 40, Width = 900, Height = 700, Maximized = true };
            s.ShowMagnifierKeyHints = false;
            s.LastSelection = new PixelRect(-1500, 30, 640, 480);
            service.Save();

            SettingsService reloaded = new SettingsService(path);
            reloaded.Load();
            foreach(PropertyInfo property in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                // ExtensionData: Rohdaten; DefaultFill: altes Feld, wird beim Laden übernommen und nicht mehr geschrieben
                if(property.Name == nameof(AppSettings.ExtensionData) || property.Name == nameof(AppSettings.DefaultFill)) continue;
                object? expected = property.GetValue(s);
                object? actual = property.GetValue(reloaded.Settings);
                if(property.PropertyType == typeof(WindowPlacement)) {
                    Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
                    continue;
                }
                Assert.True(Equals(expected, actual), $"{property.Name}: erwartet {expected}, gelesen {actual}");
                Assert.NotEqual(property.GetValue(new AppSettings()), expected);
            }
        }
    }

    [Fact]
    public void InvalidWindowPlacement_IsDropped() {
        using (TempFolder temp = new TempFolder()) {
            string path = Path.Combine(temp.Path, "settings.json");
            File.WriteAllText(path, """{ "historyWindow": { "left": 0, "top": 0, "width": 10, "height": 10 } }""");
            SettingsService service = new SettingsService(path);
            service.Load();
            Assert.Null(service.Settings.HistoryWindow);
        }
    }

    [Fact]
    public void ViewModel_ValidatesAndSaves() {
        using (TempFolder temp = new TempFolder()) {
            SettingsService service = new SettingsService(Path.Combine(temp.Path, "settings.json"));
            service.Load();
            FakeAutostart autostart = new FakeAutostart();
            int changed = 0;
            service.Changed += (s, e) => changed++;
            SettingsViewModel viewModel = new SettingsViewModel(service, autostart, null, () => false, () => new DateTime(2026, 11, 9, 14, 32, 5));
            Assert.False(viewModel.Autostart);
            Assert.Equal("Vorschau: 2026-11-09_14-32-05.png", viewModel.FileNamePreview);

            viewModel.FileNamePattern = "a/b";
            Assert.NotNull(viewModel.FileNamePatternError);
            Assert.True(viewModel.HasErrors);
            Assert.False(viewModel.Apply());
            Assert.Equal(0, changed);

            viewModel.FileNamePattern = "Shot {HH}{mm}{ss} {dd}.{MM}.{yyyy}";
            viewModel.StorageFolder = "relativ\\ordner";
            Assert.NotNull(viewModel.StorageFolderError);
            viewModel.StorageFolder = temp.Path;
            Assert.Null(viewModel.StorageFolderError);
            Assert.Equal(Path.Combine(temp.Path, "Export"), viewModel.ResolvedExportFolder);
            viewModel.RetentionDaysText = "-3";
            Assert.NotNull(viewModel.RetentionError);
            viewModel.RetentionDaysText = "30";

            // gleiche Hotkeys → Fehler, nicht gespeichert
            viewModel.HotkeyCaptureSave = "PrintScreen";
            Assert.True(viewModel.HasHotkeyErrors);
            Assert.False(viewModel.Apply());
            Assert.Equal(0, changed);

            viewModel.HotkeyCaptureSave = "Strg+Alt+S";
            viewModel.Autostart = true;
            viewModel.MagnifierZoom = 99;
            viewModel.DefaultCursorKind = CursorKinds.IBeam;
            Assert.True(viewModel.Apply());
            Assert.Equal(1, changed);
            Assert.Equal(1, autostart.Calls);
            Assert.True(autostart.IsEnabled);
            AppSettings saved = service.Settings;
            Assert.Equal("Ctrl+Alt+S", saved.HotkeyCaptureSave);
            Assert.Equal(temp.Path, saved.StorageFolder);
            Assert.Equal(30, saved.RetentionDays);
            Assert.Equal(AppSettings.MaxMagnifierZoom, saved.MagnifierZoom);
            Assert.Equal(CursorKinds.IBeam, saved.DefaultCursorKind);
            Assert.True(saved.Autostart);

            // Autostart unverändert → kein weiterer Registry-Zugriff
            Assert.True(viewModel.Apply());
            Assert.Equal(1, autostart.Calls);
        }
    }
}
