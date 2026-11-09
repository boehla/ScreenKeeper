# ScreenKeeper – verbindliche Architektur- und Arbeitsregeln

Dieses Dokument ergänzt `SPEC.md` (die fachliche Spezifikation) um technische Festlegungen,
damit alle Phasen zusammenpassen. Es gilt für jeden, der an diesem Code arbeitet.

## Ordnerstruktur

```
<Repo-Root>\
   SPEC.md, ARCHITECTURE.md
   ScreenKeeper\                 ← DAS Projekt (eine .csproj, Auslieferungs-Einheit)
      ScreenKeeper.csproj, app.manifest, App.xaml(.cs), ScreenKeeper.ico
      Models\  Services\  Services\Native\  ViewModels\  ViewModels\Undo\  Views\  Resources\
      README.md, CHANGELOG.md
   ScreenKeeper.Tests\           ← xUnit-Testprojekt (darf NuGet nutzen, gehört nicht zur Auslieferung)
```

## Projektdatei

- `net10.0-windows10.0.19041.0`, `UseWPF=true`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`.
- `UseWindowsForms=true` ist erlaubt (Framework-Bestandteil, kein NuGet) – ausschließlich für `NotifyIcon`
  und `FolderBrowserDialog`. Sonst nichts aus WinForms verwenden.
- **Keine NuGet-Pakete** im Hauptprojekt. Alles Win32 über P/Invoke in `Services\Native\`.
- `app.manifest` mit `dpiAwareness = PerMonitorV2` (plus `dpiAware true/PM` als Fallback), `supportedOS` für Win10/11.
- `<Version>` in der csproj; `<ApplicationIcon>ScreenKeeper.ico</ApplicationIcon>`.
- Quelldateien UTF-8 mit BOM, Zeilenende CRLF (siehe `.editorconfig` im Root).

## Lokalisierung

- Alle UI-Texte in `Resources\Strings.resx` (Deutsch, neutrale Kultur).
- `dotnet build` führt den ResX-Codegenerator **nicht** aus. Deshalb: eine handgepflegte statische Klasse
  `Resources\Strings.cs` mit `ResourceManager` und einer Eigenschaft je Schlüssel (`public static string Capture => rm.GetString("Capture")!;`),
  plus eine XAML-Markup-Extension `{loc:Str Key}` (`Resources\LocExtension.cs`).
  Jeder neue Text: Eintrag in der .resx UND Eigenschaft in `Strings.cs`.

## Datenmodell (`Models\`)

- `CaptureDocument` exakt nach SPEC Abschnitt 4. Alle Koordinaten in **Bildpixeln** (int).
- `PixelRect { X, Y, Width, Height }` und `PixelPoint { X, Y }` als eigene kleine record structs (int).
- `Annotation` ist abstrakt; Unterklassen: `RectangleAnnotation`, `EllipseAnnotation`, `LineAnnotation`, `ArrowAnnotation`,
  `FreehandAnnotation`, `TextAnnotation`, `HighlightAnnotation`, `PixelateAnnotation`, `BlurAnnotation`, `NumberStampAnnotation`,
  `CursorAnnotation`. Jede hat `Id` (string, Guid "N") und `Type`-Diskriminator.
- JSON über `System.Text.Json`: `[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` + `[JsonDerivedType]`,
  `JsonSerializerOptions` mit `AllowOutOfOrderMetadataProperties = true`, camelCase, `WriteIndented = true`,
  `DefaultIgnoreCondition = WhenWritingNull`. Unbekannte Annotation-Typen beim Lesen **nicht** werfen lassen –
  stattdessen vorher per `JsonDocument` filtern und in `UnknownAnnotation` (Rohdaten als `JsonElement`) aufheben, damit sie
  beim Speichern unverändert zurückgeschrieben werden.
- Farben als `#AARRGGBB`-Strings im Modell. `formatVersion` = 1.
- Das Modell hat **keine** WPF-Abhängigkeit (kein `System.Windows.*`), damit Tests leicht bleiben.

## Services (`Services\`)

Alle Services als Klassen mit Interface (`ICaptureService` usw.), ohne DI-Framework. Komposition in `App.xaml.cs`
über eine kleine statische Klasse `AppServices` (Eigenschaften je Service, in `OnStartup` befüllt).

- `SettingsService`: `%APPDATA%\ScreenKeeper\settings.json`, Klasse `AppSettings`; Laden tolerant (fehlende Werte → Standard),
  Speichern atomar. Ereignis `Changed`.
- `StorageService`: Ablageordner (Standard: Known Folder *Pictures*\ScreenKeeper), Unterordner `JJJJ\MM`,
  Dateinamen-Muster mit Platzhaltern, Kollisionssuffix `_2`, `_3` …; `SaveNewCapture` (PNG + JSON + Thumb),
  `SaveDocument` (JSON atomar: `.tmp` + `File.Move(overwrite)`), `UpdateThumbnail`, `LoadDocument`, `EnumerateCaptures`
  (lazy, neueste zuerst), `DeleteToRecycleBin` (SHFileOperation, FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT).
- `CaptureService`: BitBlt von `GetDC(NULL)` über den virtuellen Bildschirm (physische Pixel, Prozess ist PerMonitorV2)
  → `BitmapSource` (Bgra32, eingefroren). Ohne Cursor. Zusätzlich Monitore (`EnumDisplayMonitors`/`GetMonitorInfo`,
  Skalierung über `GetDpiForMonitor`), Cursor (`GetCursorInfo`, `GetIconInfo`, Farb- und Maskenbitmap → PNG-Bytes mit
  Transparenz, monochrome Cursor behandeln), Fenster (`EnumWindows`, sichtbar, nicht cloaked (`DWMWA_CLOAKED`),
  `DWMWA_EXTENDED_FRAME_BOUNDS`, Titel, Prozessname). Ergebnis `CaptureResult { Bitmap, Document }`.
- `RenderService`: **der eine Render-Pfad**. `Drawing BuildAnnotationDrawing(CaptureDocument doc, BitmapSource original)`
  liefert die Zeichnungen (inkl. Pixelate/Blur aus dem Original berechnet, inkl. Cursor-Annotationen) als
  `DrawingGroup` in Bildpixel-Koordinaten. `BitmapSource Render(CaptureDocument doc, BitmapSource original, PixelRect region)`
  zeichnet Original + Annotation-Drawing per `DrawingVisual`/`RenderTargetBitmap` (96 dpi, Pbgra32) und schneidet auf `region`.
  Der Editor zeigt dasselbe `Drawing` live an (z. B. über ein `DrawingImage`/`DrawingBrush` oder ein Host-Element) –
  so ist Anzeige = Export.
- `ClipboardService`: Bild als `CF_BITMAP`/DIB **und** als `PNG`-Format setzen (`DataObject` mit `SetImage` + `SetData("PNG", MemoryStream)`),
  mit Wiederholversuchen (Zwischenablage kann belegt sein).
- `HotkeyService`: `RegisterHotKey` über ein verstecktes `HwndSource`-Fenster; Fallback `WH_KEYBOARD_LL` für die Druck-Taste.
  Hotkey-Modell `HotkeyBinding { Modifiers, Key }` serialisierbar als String (`"Ctrl+Shift+PrintScreen"`).
- `AutostartService`: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, Wert `ScreenKeeper` = `"<Pfad zur EXE>"`.
- `EnvironmentCheckService`: Hinweise erkennen (Snipping-Registry-Wert, Prozess `Greenshot`).

## Editor (`ViewModels\EditorViewModel`, `Views\EditorWindow`)

- Arbeitet ausschließlich auf `CaptureDocument`; alle Änderungen gehen durch `UndoStack` (`ViewModels\Undo\`, `IUndoableCommand`
  mit `Do()`/`Undo()`), mindestens 100 Schritte.
- Zwei Darstellungsmodi mit **derselben** Oberfläche: `Fullscreen` (rahmenlos, Topmost, über den virtuellen Bildschirm,
  1:1 physische Pixel, Fensterposition per `SetWindowPos` in physischen Pixeln) und `Windowed` (maximiert, Zoom per Strg+Mausrad,
  Verschieben mit Leertaste+Ziehen oder mittlerer Maustaste). Die Zeichenfläche ist ein Element in Bildpixel-Koordinaten unter einer
  `ScaleTransform`/`TranslateTransform` (Zoom/Pan); im Vollbild ist Zoom = 1 / DPI-Skalierung, damit 1 Bildpixel = 1 Gerätepixel.
  `RenderOptions.BitmapScalingMode=NearestNeighbor`, `UseLayoutRounding=true`, `SnapsToDevicePixels=true`.
- Beim Schließen (auch Esc) immer: Dokument speichern + Thumbnail aktualisieren.

## Kommandozeile (für automatisierte Tests, nicht in der README bewerben)

`App` wertet `Environment.GetCommandLineArgs()` aus; mit einem dieser Schalter läuft **kein** Tray/Mutex, die App arbeitet
headless und beendet sich mit Exit-Code 0 (Erfolg) bzw. 1 (Fehler, Meldung auf stderr):

- `--capture [--no-editor] [--folder <Ablageordner>]` → Aufnahme machen, PNG/JSON/Thumb speichern, die drei Pfade auf stdout
  ausgeben (je eine Zeile). Ohne `--no-editor` zusätzlich den Editor öffnen.
- `--render <json> <out.png> [--region selection|full|monitor:<index>]` → Dokument laden, rendern, PNG schreiben.
- `--open <json>` → Editor für vorhandene Aufnahme öffnen (wie aus der History).
- `--version` → Version auf stdout.

## Code-Stil (verbindlich)

- Klammern K&R (öffnende Klammer auf derselben Zeile), `if(x) {` ohne Leerzeichen vor `(` bei Schlüsselwörtern,
  `} catch(Exception ex) {`. Private Felder camelCase **ohne** Unterstrich, private Methoden camelCase,
  öffentliche Member PascalCase. Explizite Typen statt `var`. `using (…) { }`-Blöcke statt using-Deklarationen.
  Catch-Variable immer `ex`. Keine Blockkommentare. Kommentare und UI-Texte Deutsch, Bezeichner Englisch.
- Keine absoluten Pfade, keine Benutzer- oder Rechnernamen im Code.
- Jede Phase: `CHANGELOG.md` ergänzen.
