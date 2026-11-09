using ScreenKeeper.Services;

namespace ScreenKeeper.Tests;

public class SettingsServiceTests {
    [Fact]
    public void MissingFile_GivesDefaults() {
        using (TempFolder temp = new TempFolder()) {
            SettingsService service = new SettingsService(Path.Combine(temp.Path, "settings.json"));
            service.Load();
            AppSettings settings = service.Settings;
            Assert.Equal("", settings.StorageFolder);
            Assert.Equal(AppSettings.DefaultFileNamePattern, settings.FileNamePattern);
            Assert.Equal("PrintScreen", settings.HotkeyCaptureEdit);
            Assert.Equal("Ctrl+PrintScreen", settings.HotkeyCaptureSave);
            Assert.Equal("Shift+PrintScreen", settings.HotkeyHistory);
            Assert.Equal(8, settings.MagnifierZoom);
            Assert.True(settings.CursorVisible);
            Assert.False(settings.Autostart);
            Assert.Equal(30, settings.RetentionDays);
            Assert.True(settings.CheckForUpdates);
            Assert.True(settings.NotifyOnSaveOnly);
        }
    }

    [Fact]
    public void PartialFile_WithUnknownFields_LoadsAndKeepsUnknown() {
        using (TempFolder temp = new TempFolder()) {
            string path = Path.Combine(temp.Path, "settings.json");
            File.WriteAllText(path, """
                {
                  // Kommentar ist erlaubt
                  "magnifierZoom": 12,
                  "hotkeyHistory": "Ctrl+Alt+H",
                  "fileNamePattern": null,
                  "neuesFeldAusZukunft": { "a": [1, 2, 3] },
                  "notifyOnSaveOnly": false,
                }
                """);
            SettingsService service = new SettingsService(path);
            service.Load();
            AppSettings settings = service.Settings;
            Assert.Equal(12, settings.MagnifierZoom);
            Assert.Equal("Ctrl+Alt+H", settings.HotkeyHistory);
            Assert.Equal("PrintScreen", settings.HotkeyCaptureEdit);
            Assert.Equal(AppSettings.DefaultFileNamePattern, settings.FileNamePattern);
            Assert.False(settings.NotifyOnSaveOnly);
            Assert.True(settings.CursorVisible);
            // Feld fehlt (Dateien bis 0.6 ohne Eintrag) → neuer Standard 30 Tage
            Assert.Equal(AppSettings.DefaultRetentionDays, settings.RetentionDays);

            service.Save();
            string written = File.ReadAllText(path);
            Assert.Contains("neuesFeldAusZukunft", written);
            Assert.Contains("\"magnifierZoom\": 12", written);
        }
    }

    [Fact]
    public void OutOfRangeValues_AreNormalized() {
        using (TempFolder temp = new TempFolder()) {
            string path = Path.Combine(temp.Path, "settings.json");
            File.WriteAllText(path, """{ "settingsVersion": 1, "magnifierZoom": 99, "retentionDays": -5, "defaultThickness": 0 }""");
            SettingsService service = new SettingsService(path);
            service.Load();
            Assert.Equal(AppSettings.MaxMagnifierZoom, service.Settings.MagnifierZoom);
            Assert.Equal(0, service.Settings.RetentionDays);
            Assert.Equal(4, service.Settings.DefaultThickness);
        }
    }

    [Fact]
    public void BrokenFile_GivesDefaults() {
        using (TempFolder temp = new TempFolder()) {
            string path = Path.Combine(temp.Path, "settings.json");
            File.WriteAllText(path, "{ das ist kein JSON");
            SettingsService service = new SettingsService(path);
            service.Load();
            Assert.Equal(8, service.Settings.MagnifierZoom);
            Assert.True(File.Exists(path + ".broken"));
        }
    }

    [Fact]
    public void Save_RaisesChanged() {
        using (TempFolder temp = new TempFolder()) {
            SettingsService service = new SettingsService(Path.Combine(temp.Path, "sub", "settings.json"));
            service.Load();
            int raised = 0;
            service.Changed += (s, e) => raised++;
            service.Settings.RetentionDays = 45;
            service.Save();
            Assert.Equal(1, raised);

            SettingsService reloaded = new SettingsService(service.FilePath);
            reloaded.Load();
            Assert.Equal(45, reloaded.Settings.RetentionDays);
        }
    }

    [Fact]
    public void OldSettingsFile_WithoutVersion_MigratesRetentionZeroTo30() {
        using (TempFolder temp = new TempFolder()) {
            string path = Path.Combine(temp.Path, "settings.json");
            // Datei aus 0.6.x: kein settingsVersion, retentionDays 0 war der damalige Standard „aus“
            File.WriteAllText(path, "{ \"retentionDays\": 0, \"magnifierZoom\": 8 }");
            SettingsService service = new SettingsService(path);
            service.Load();
            Assert.Equal(30, service.Settings.RetentionDays);
            Assert.Equal(AppSettings.CurrentSettingsVersion, service.Settings.SettingsVersion);
            // die umgestellte Datei wurde sofort zurückgeschrieben
            Assert.Contains("\"settingsVersion\": 1", File.ReadAllText(path));
            Assert.Contains("\"retentionDays\": 30", File.ReadAllText(path));

            // Datei ab 0.7.0 mit bewusst abgeschalteter Aufbewahrung bleibt aus
            File.WriteAllText(path, "{ \"settingsVersion\": 1, \"retentionDays\": 0 }");
            service.Load();
            Assert.Equal(0, service.Settings.RetentionDays);

            // Alte Datei mit bewusst gesetztem anderen Wert bleibt erhalten
            File.WriteAllText(path, "{ \"retentionDays\": 90 }");
            service.Load();
            Assert.Equal(90, service.Settings.RetentionDays);
        }
    }
}
