# ScreenKeeper – Abgleich mit der Spezifikation (Version 0.6.0)

Stand: 09.11.2026. Geprüft am Code (nicht am Bericht), ergänzt um automatische Tests (205 xUnit-Tests), die
Headless-Testschalter (siehe `TESTS.md`) und einen echten Tray-Lauf mit simulierter Druck-Taste (`SendInput`, Greenshot
lief parallel, dadurch Tastatur-Hook-Pfad). Testrechner: ein Monitor 1920 × 1080, 100 %, Remotedesktop.

Status: **erfüllt** = im Code umgesetzt und automatisch bzw. im Lauf bestätigt · **teilweise** = Teil der Anforderung
fehlt · **fehlt** · **nur manuell prüfbar** = umgesetzt, aber nur mit Hardware/Programmen prüfbar, die hier nicht zur
Verfügung standen (zweiter Monitor, 150 %, Paint/Outlook …) – mit Prüfanleitung.
Pfade relativ zu `ScreenKeeper\`.

## Abschnitt 2 – Rahmenbedingungen

| Anforderung | Status | Fundstelle | Anmerkung |
|---|---|---|---|
| Nur Windows 10 (ab 2004) / 11, x64 | erfüllt | `app.manifest`, `ScreenKeeper.csproj` | `supportedOS` Win10/11, TFM 10.0.19041 (= 2004); AnyCPU läuft auf x64 als 64-Bit |
| C#, WPF, MVVM (Views/ViewModels/Models/Services) | erfüllt | Ordnerstruktur | Modell ohne `System.Windows` |
| .NET 10, `net10.0-windows10.0.19041.0` | erfüllt | `ScreenKeeper.csproj` | |
| Keine NuGet-Pakete, Win32 per P/Invoke | erfüllt | `ScreenKeeper.csproj`, `Services\Native\` | NuGet nur im Testprojekt |
| DPI `PerMonitorV2`, V1 gleiche Skalierung, Modell in Bildpixeln | nur manuell prüfbar | `app.manifest`, `Views\EditorWindow.xaml.cs`, `Services\ScreenCoordinates.cs` | Zoom = 1/DPI, `SetWindowPos` physisch, Neu-Platzierung nach `DpiChanged` (0.6.0); Rechnung mit 150 % in `Phase6Tests.Dpi150_…` · Prüfung: alle Monitore auf 150 % stellen, Druck → Bild muss scharf 1:1 liegen, Lupe/Werkzeugleiste in normaler Größe, ein mit Pfeiltasten gewählter 1-px-Rand landet exakt in der Zwischenablage |
| Oberfläche Deutsch, Texte zentral in Ressourcendatei | erfüllt | `Resources\Strings.resx`, `Resources\Strings.cs` | 187 Texte, `StringsTests`; ungenutzter Text `AppName` entfernt |
| Einstellungen in `%APPDATA%\ScreenKeeper\settings.json`, keine absoluten Pfade, Registry nur Autostart | erfüllt | `Services\AppPaths.cs`, `Services\SettingsService.cs`, `Services\AutostartService.cs` | Registry sonst nur lesend (Snipping-Hinweis) |
| Eine Instanz je Benutzer (Mutex), zweiter Start holt Hauptfenster nach vorne | nur manuell prüfbar | `Services\SingleInstance.cs`, `App.xaml.cs` | Mutex mit Benutzer-SID, Broadcast-Nachricht · Prüfung: ScreenKeeper zweimal starten → nur ein Tray-Symbol, die History der ersten Instanz kommt nach vorne |
| Quelldateien UTF-8 mit BOM, CRLF | erfüllt | `.editorconfig` | Prüfskript: 0 Abweichungen (`.md`/`.resx`/`.csproj` ohne BOM laut `.editorconfig`) |
| Tests für Datenmodell/JSON | erfüllt | `ScreenKeeper.Tests\` | 205 Tests, deutlich über das Datenmodell hinaus |

## Abschnitt 3.1 – Tray und Hotkeys

| Anforderung | Status | Fundstelle | Anmerkung |
|---|---|---|---|
| Tray-Symbol, Menü Aufnahme / History / Einstellungen / Beenden, Doppelklick = History | nur manuell prüfbar | `Views\TrayIcon.cs`, `App.xaml.cs` | Prüfung: Rechtsklick aufs Symbol zeigt die vier Einträge (Aufnahme fett), Doppelklick öffnet die History |
| Hotkeys frei konfigurierbar, Standard Druck / Strg+Druck / Shift+Druck | erfüllt | `Services\AppSettings.cs`, `Services\HotkeyBinding.cs`, `Views\SettingsWindow.xaml` | `HotkeyBindingTests`, `SettingsPhase5Tests` |
| Strg+Druck: nur speichern + kurze Tray-Benachrichtigung | erfüllt | `App.xaml.cs` (`captureSaveOnly`) | Bei offenem Editor holt Strg+Druck seit 0.6.0 den Editor nach vorne statt ihn zu fotografieren |
| `RegisterHotKey`, sonst `WH_KEYBOARD_LL` für Druck | erfüllt | `Services\HotkeyService.cs`, `Services\KeyboardHook.cs` | Hook seit 0.6.0 in eigenem Thread; nur konfigurierte Druck-Kombinationen, Drücken und Loslassen unterdrückt (`Phase6Tests.PrintScreenFilter_…`); Tray-Lauf: 3 von 3 Drücken erkannt (vorher 2 von 3) |
| Hinweis Snipping-Einstellung (`PrintScreenKeyForSnippingEnabled`) und Greenshot/anderes Programm | erfüllt | `Services\EnvironmentCheckService.cs`, `App.xaml.cs` | Nur Hinweis (Balloon + Protokoll), nichts wird geändert; im Lauf erschien „Greenshot läuft …“ |

## Abschnitt 3.2 – Aufnahme und Backup

| Anforderung | Status | Fundstelle | Anmerkung |
|---|---|---|---|
| Ganzer virtueller Bildschirm ohne Mauszeiger in einem Durchgang | erfüllt | `Services\CaptureService.cs` | ein `BitBlt` über den virtuellen Bildschirm (physische Pixel), Zeiger nicht enthalten; `--capture` ok |
| Monitore: Name, Bereich, primär, Skalierung | erfüllt | `Services\CaptureService.cs` (`GetMonitors`) | Skalierung über `GetDpiForMonitor` |
| Mauszeiger: Bild mit Transparenz, Position, Hotspot, sichtbar | erfüllt | `Services\CursorImage.cs`, `Services\CaptureService.cs` | auch monochrome/invertierende Zeiger; GDI-Handles werden freigegeben |
| Sichtbare Top-Level-Fenster: Bereich (DWM-Rahmen), Titel, Prozess, Z-Reihenfolge | erfüllt | `Services\CaptureService.cs` (`captureWindows`) | ohne verborgene (cloaked), minimierte und eigene Fenster; Lauf: 18 Fenster |
| Sofort speichern vor dem Editor, `<Ablage>\JJJJ\MM\` PNG/JSON/thumb.jpg, Original nie überschrieben | erfüllt | `Services\StorageService.cs` (`SaveNewCapture`) | JSON synchron, PNG und Vorschaubild im Hintergrund (Spezifikation erlaubt das); eindeutiger Name |
| Standard-Ablageordner Bilder\ScreenKeeper, konfigurierbar | erfüllt | `Services\AppPaths.cs`, `Services\StorageService.cs` | Known Folder über `Environment.SpecialFolder.MyPictures` |
| Dateinamen-Muster mit Platzhaltern, Suffix `_2`, `_3` … | erfüllt | `Services\StorageService.cs`, `Services\CaptureFileName.cs` | auch Muster ohne Sekunden (`Phase6Tests.SaveNewCapture_PatternWithoutSeconds_…`) |
| Vorschaubild max. 320 px breit | erfüllt | `Services\StorageService.cs` | `StorageServiceTests`, auch 7680 × 2160 |
| Editor < 300 ms nach Tastendruck bei 2 × Full-HD | nur manuell prüfbar | `Services\CaptureWorkflow.cs`, `Views\EditorWindow.xaml.cs` | 1 × Full-HD gemessen: erste Aufnahme nach Start 251–324 ms (mit Vorwärmen, vorher 606–771), danach 142–192 ms · Prüfung: an 2 × Full-HD `ScreenKeeper.exe --capture --test-capture-timing --folder %TEMP%\sk` – alle `visibleMs` < 300 |

## Abschnitt 3.3 – Bearbeiten-Modus

| Anforderung | Status | Fundstelle | Anmerkung |
|---|---|---|---|
| Rahmenloses, oberstes Fenster über den ganzen virtuellen Bildschirm, Bild 1:1 | erfüllt | `Views\EditorWindow.xaml.cs` (`placeOverVirtualScreen`) | Lauf: Fenster 0,0–1920,1080, topmost |
| Außerhalb des Bereichs ca. 50 % abgedunkelt | nur manuell prüfbar | `Views\SelectionOverlay.cs` | Prüfung: Bereich aufziehen – außen sichtbar halb abgedunkelt, innen unverändert |
| Werkzeugleiste unter/oberhalb des Bereichs, vorher oben am primären Monitor | erfüllt | `ViewModels\ToolbarPlacement.cs`, `Views\EditorWindow.xaml.cs` | `PlacementTests`, zwei Monitore in `Phase6Tests` |
| Bereich aufziehen, 8 Anfasser, verschieben, neu aufziehen | erfüllt | `ViewModels\Tools\SelectionTool.cs`, `ViewModels\SelectionGeometry.cs` | `SelectionGeometryTests`, `EditorToolTests` |
| Lupe 8-fach (4–16), Raster, Fadenkreuz, Position/Größe/Farbe, weicht aus, bleibt im Monitor | erfüllt | `Views\Magnifier.xaml(.cs)`, `ViewModels\MagnifierPlacement.cs` | liest aus dem Original; Tests für Ausweichen und Monitorgrenze (auch linker Monitor, 150 %) |
| Pfeiltasten 1 Pixel, Shift 10 | erfüllt | `Views\EditorWindow.Input.cs`, `ViewModels\SelectionGeometry.cs` | echter Mauszeiger wird über `ScreenCoordinates` mitgeführt (negative Koordinaten getestet) |
| Leertaste/Enter setzt Start-/Endpunkt | erfüllt | `Views\EditorWindow.Input.cs`, `ViewModels\Tools\SelectionTool.cs` | `EditorToolTests` |
| Auswahl/Verschieben: anklicken, verschieben, Größe, Entf | erfüllt | `ViewModels\Tools\ObjectSelectTool.cs`, `ViewModels\Tools\ObjectManipulator.cs` | `--test-annotate` |
| Rechteck, Ellipse: Rahmenfarbe, Strichstärke, Füllung | erfüllt | `ViewModels\Tools\RectangleTool.cs`, `EllipseTool.cs` | |
| Linie, Pfeil: Farbe, Strichstärke, Shift = 45° | erfüllt | `ViewModels\Tools\LineTool.cs`, `ArrowTool.cs`, `Services\AnnotationGeometry.cs` | `AnnotationGeometryTests` |
| Freihand: Farbe, Strichstärke, Glättung | erfüllt | `ViewModels\Tools\FreehandTool.cs` | |
| Text: Schrift, Größe, Farbe, Hintergrund; Doppelklick bearbeitet | teilweise | `ViewModels\Tools\TextTool.cs`, `Views\EditorWindow.TextEditing.cs` | Größe, Farbe, Hintergrund, Doppelklick vorhanden; **Schriftart nicht wählbar** (fest Segoe UI, `fontFamily`/`bold` nur im Datenmodell) |
| Highlight (ab 0.11 Multiplizieren statt halbtransparent), Standard Gelb | erfüllt | `ViewModels\Tools\HighlightTool.cs`, `Services\RenderService.cs`, `Services\ImageEffects.cs` | `HighlightMultiplyTests` |
| Verpixeln mit Blockgröße | erfüllt | `ViewModels\Tools\PixelateTool.cs`, `Services\ImageEffects.cs` | |
| Weichzeichnen mit Stärke | erfüllt | `ViewModels\Tools\BlurTool.cs`, `Services\ImageEffects.cs` | |
| Nummern-Stempel fortlaufend, Farbe, Größe | erfüllt | `ViewModels\Tools\NumberStampTool.cs` | ab 0.12 Nummer des ausgewählten Stempels mit „−“/„+“ bzw. Tasten -/+ änderbar (`ViewModels\EditorViewModel.Annotations.cs`, `StampNumberTests`) |
| Farbe/Strichstärke über Werkzeugleiste, zuletzt verwendete gemerkt | erfüllt | `Views\EditorToolbar.xaml`, `ViewModels\ToolOptions.cs` | beim Schließen in den Einstellungen |
| Rückgängig/Wiederholen (Strg+Z/Strg+Y) für alles inkl. Bereich und Maus | erfüllt | `ViewModels\Undo\` | 200 Schritte; `AnnotationCommandTests` (60 Schritte) |
| Verpixeln/Weichzeichnen nicht destruktiv, aus dem Original berechnet | erfüllt | `Services\RenderService.cs`, `Services\ImageEffects.cs` | `ImageEffectsTests` |
| Zeichnungen außerhalb des Bereichs bleiben, Export schneidet ab | erfüllt | `Services\RenderService.cs` (`Render`) | |
| Echter Mauszeiger nie im Original-PNG | erfüllt | `Services\CaptureService.cs` | |
| Aufgenommener Zeiger als Objekt an Originalposition, standardmäßig sichtbar (Einstellung) | erfüllt | `Services\CaptureWorkflow.cs` (`addCapturedCursor`) | |
| Zeiger verschieben, ausblenden, löschen, neu einfügen; Arten aufgenommen/Pfeil/Hand/Textcursor | erfüllt | `ViewModels\Tools\CursorTool.cs` | |
| Bereich → Zwischenablage (Enter, Strg+C) | erfüllt | `ViewModels\EditorViewModel.cs`, `Views\EditorWindow.Input.cs` | `--test-select`: 300 × 200 in der Zwischenablage |
| Bereich speichern (Strg+S) im Exportordner | erfüllt | `ViewModels\EditorViewModel.cs` (`SaveSelection`) | Kollisionssuffix |
| Bereich speichern unter … (Strg+Shift+S), PNG oder JPG | nur manuell prüfbar | `ViewModels\EditorViewModel.cs`, `Views\EditorWindow.xaml.cs` | Prüfung: Strg+Shift+S, als `.jpg` speichern – Dialog liegt über dem Editor, Datei ist ein JPEG des Bereichs |
| Ganzer Bildschirm → Zwischenablage (Strg+Shift+C) | erfüllt | `ViewModels\EditorViewModel.cs` (`CopyFull`) | |
| Aktueller Monitor → Zwischenablage | nur manuell prüfbar | `ViewModels\EditorViewModel.cs` (`CopyMonitor`) | Monitorsuche mit zwei Monitoren getestet · Prüfung: an zwei Monitoren Knopf auf dem linken Monitor klicken – Zwischenablage enthält genau diesen Monitor |
| Abbrechen (Esc) | erfüllt | `Views\EditorWindow.Input.cs` | bricht zuerst laufendes Ziehen bzw. Auswahl ab |
| Bei jedem Beenden (auch Esc) JSON + Vorschaubild speichern | erfüllt | `Views\EditorWindow.xaml.cs` (`onClosing`) | Esc, Knöpfe, Alt+F4, WM_CLOSE und Programmende (WPF schließt alle Fenster mit `Closing`); Fehler jetzt mit Meldung |
| Ohne Bereich Bereich-Knöpfe deaktiviert | erfüllt | `ViewModels\EditorViewModel.cs` (`CanExecute`) | |
| Zwischenablage: Bitmap **und** PNG | erfüllt | `Services\ClipboardService.cs` | geprüft: Formate `Bitmap`, `PNG`; PNG 300 × 200, 32 bpp mit Alpha; 10 Wiederholversuche, Fehler als Meldung |

## Abschnitt 3.4 – History und nachträgliches Bearbeiten

| Anforderung | Status | Fundstelle | Anmerkung |
|---|---|---|---|
| Hauptfenster mit Vorschaubildern, neueste zuerst, nach Tagen gruppiert, Datumsfilter, Nachladen beim Scrollen | erfüllt | `Views\HistoryWindow.xaml`, `ViewModels\HistoryViewModel.cs`, `HistoryGrouping.cs`, `HistoryPager.cs` | `--test-history` mit 5.000 Einträgen: erster Block 661 ms, alle 3.566 ms, 0 Bindungsfehler |
| Je Eintrag Vorschaubild, Uhrzeit, Kennzeichen „bearbeitet“ | erfüllt | `ViewModels\HistoryRows.cs`, `Models\CaptureDocument.cs` (`IsEdited`) | |
| Bearbeiten (Doppelklick), Zwischenablage, Explorer, Löschen in den Papierkorb (alle Dateien) | erfüllt | `ViewModels\HistoryViewModel.cs`, `Services\StorageService.cs` | `--test-edit`, `--test-delete` (filesLeft=0) |
| Bearbeiten öffnet denselben Editor; gleiche Anordnung → Vollbild, sonst maximiertes Fenster mit Zoom/Pan | erfüllt | `Services\CaptureWorkflow.cs`, `Services\ScreenCoordinates.cs` (`IsSameLayout`) | Test für Anordnung; 2 × 4K-Aufnahme öffnete im Fenster-Modus (25 %) |
| Aufnahmen ohne JSON werden angezeigt und bekommen beim Bearbeiten eine JSON | erfüllt | `Services\StorageService.cs` (`OpenOrCreate`) | defekte JSON wird als `.json.broken` gesichert |
| Automatisches Löschen nach X Tagen (Standard aus), Papierkorb | erfüllt | `Services\RetentionService.cs` | Tests mit Probelauf |

## Abschnitt 3.5 – Einstellungen

| Anforderung | Status | Fundstelle | Anmerkung |
|---|---|---|---|
| Ablageordner, Dateinamen-Muster, Exportordner | erfüllt | `ViewModels\SettingsViewModel.cs`, `Views\SettingsWindow.xaml` | Muster-Prüfung mit Vorschau |
| Hotkeys mit Konfliktprüfung | erfüllt | `ViewModels\HotkeyConflictChecker.cs` | `SettingsPhase5Tests` |
| Lupe (Zoom, Größe) | erfüllt | `Services\AppSettings.cs`, `ViewModels\SettingsViewModel.cs` | |
| Standardfarben und Strichstärke | erfüllt | ebenda | |
| Mauszeiger standardmäßig sichtbar | erfüllt | ebenda | |
| Autostart mit Windows (`HKCU\…\Run`) | nur manuell prüfbar | `Services\AutostartService.cs` | Prüfung: Häkchen setzen → Wert `ScreenKeeper` unter `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, Häkchen entfernen → Wert weg |
| Autostart mit Adminrechten (Aufgabenplanung, ab 0.12.0) | nur manuell prüfbar | `Services\ElevatedAutostartService.cs`, `ViewModels\SettingsViewModel.cs` | XML, Argumente, Startlogik und Einstellungslogik in `Version0120Tests` · Prüfung: (1) Häkchen setzen, Übernehmen → genau eine UAC-Abfrage, danach Aufgabe `ScreenKeeper\Autostart-<SID>` in der Aufgabenplanung, kein Wert `ScreenKeeper` unter `HKCU\…\Run`, „Mit Windows starten“ ausgegraut; erneut Übernehmen → keine UAC-Abfrage. (2) UAC-Abfrage mit „Nein“ beantworten → Häkchen wieder aus, keine Fehlermeldung. (3) Abmelden/Anmelden (oder ScreenKeeper beenden und aus dem Startmenü starten) → Protokoll „gestartet (mit Adminrechten)“. (4) Admin-PowerShell im Vordergrund, Druck → Editor erscheint. (5) Bereich kopieren und in Paint (ohne Adminrechte) einfügen → Bild kommt an. (6) Ablageordner aus der History öffnen → Explorer läuft ohne Adminrechte (Task-Manager, Spalte „Mit erhöhten Rechten“). (7) Startmenü-Start bei laufender erhöhter Instanz → History kommt nach vorne, keine zweite Instanz. (8) Häkchen entfernen, Übernehmen → eine UAC-Abfrage, Aufgabe weg, `Run`-Wert wieder da (wenn „Mit Windows starten“ gewünscht) |
| Aufbewahrungsdauer | erfüllt | `Services\RetentionService.cs` | |
| Tray-Benachrichtigung bei „nur speichern“ ein/aus | erfüllt | `App.xaml.cs`, `Services\AppSettings.cs` | |

## Abschnitt 4 – Datenformat

| Anforderung | Status | Fundstelle | Anmerkung |
|---|---|---|---|
| Alle Koordinaten in Bildpixeln (int), `monitors`/`windows` über `virtualScreen` umgerechnet | erfüllt | `Models\PixelRect.cs`, `Models\PixelPoint.cs`, `Services\ScreenCoordinates.cs` | Tests mit virtuellem Bildschirm −1920,0 / 3840 × 1080 und Monitor oberhalb |
| `formatVersion`; ältere lesbar, Erweiterungen möglich | erfüllt | `Models\CaptureDocumentSerializer.cs` | höhere Version wird gelesen und protokolliert (0.6.0), Versionsnummer bleibt erhalten |
| Schreiben atomar (temporäre Datei + Umbenennen) | erfüllt | `Services\AtomicFile.cs` | auch PNG, Vorschaubild, Einstellungen |
| Beispiel-JSON lädt und speichert verlustfrei | erfüllt | `Models\` | `Phase6Tests.SpecExample_RoundtripIsLossless`: alle Werte inkl. `ocrText: null` und Reihenfolge; dazu kommen nur Standardwerte (`visible: true`) |
| Unbekannte Felder und Typen bleiben erhalten | erfüllt | `Models\AnnotationListConverter.cs`, `Models\UnknownAnnotation.cs`, `Models\Annotation.cs` | seit 0.6.0 auch Felder in Zeichenobjekten, Monitoren, Fenstern, Zeiger |
| `annotations` polymorph (`type`), Zeichenreihenfolge | erfüllt | `Models\Annotation.cs` | |

## Abschnitt 9 – Übergabe

| Anforderung | Status | Fundstelle | Anmerkung |
|---|---|---|---|
| Ein Projektordner mit einer `.csproj`, `dotnet build` ohne weitere Schritte | erfüllt | `ScreenKeeper.csproj` | Build 0 Warnungen, 0 Fehler |
| Keine absoluten Pfade, keine Rechner-/Benutzerabhängigkeit | erfüllt | gesamter Code | Suche nach Laufwerkspfaden ohne Treffer; Pfade über Known Folders |
| `<Version>` in der csproj, `CHANGELOG.md` je Phase | erfüllt | `ScreenKeeper.csproj`, `CHANGELOG.md` | 0.6.0; Phasen 4 und 5 gemeinsam unter 0.5.0 |
| `README.md` mit Kurzbeschreibung, Build, Bedienung (Hotkeys, Knöpfe) | erfüllt | `README.md` | mit dem Code abgeglichen (Hotkeys, Knöpfe, Werkzeug-Tasten) |
| Programmsymbol `.ico` im Projektordner | erfüllt | `ScreenKeeper.ico` | |
| Aus beliebigem Ordner lauffähig, schreibt nichts neben die EXE | erfüllt | `Services\AppPaths.cs` | nur `%APPDATA%\ScreenKeeper\` und Ablageordner |

## Abschnitt 7 – Abnahmekriterien

| Anforderung | Status | Fundstelle | Anmerkung |
|---|---|---|---|
| P1: Druck löst Aufnahme aus, auch bei Explorer, Browser, Vollbild-Anwendung | nur manuell prüfbar | `Services\HotkeyService.cs`, `Services\KeyboardHook.cs` | per `SendInput` mit fremdem Programm im Vordergrund geprüft · Prüfung: Explorer, Browser und ein Video/Spiel im Vollbild jeweils vorne, Druck → Editor erscheint vorne und reagiert auf Tasten |
| P1: Hinweis bei Snipping-Einstellung oder Greenshot | erfüllt | `Services\EnvironmentCheckService.cs` | im Lauf mit Greenshot erschienen |
| P1: PNG mit allen Monitoren, ohne Zeiger, negative Koordinaten korrekt | nur manuell prüfbar | `Services\CaptureService.cs`, `Services\ScreenCoordinates.cs` | Umrechnung getestet · Prüfung: zweiten Monitor links vom primären anordnen, Druck, PNG ansehen – beide Monitore lückenlos nebeneinander, kein Zeiger, JSON `virtualScreen.x` negativ |
| P1: JSON und Vorschaubild daneben, JSON gültig mit Monitoren, Fenstern, Maus | erfüllt | `Services\StorageService.cs` | `--capture --no-editor` |
| P2: Editor < 300 ms (2 × Full-HD) | nur manuell prüfbar | siehe 3.2 | Prüfung: `--capture --test-capture-timing` an 2 × Full-HD |
| P2: Lupe mit Raster, Fadenkreuz, Koordinaten, Größe, Farbe; Pfeiltasten 1 bzw. 10 Pixel | erfüllt | `Views\Magnifier.xaml.cs`, `ViewModels\SelectionGeometry.cs` | |
| P2: 1-Pixel-genau gewählter Bereich ergibt exakt diese Pixel in der Zwischenablage | nur manuell prüfbar | `Services\RenderService.cs` | pixelgenaues Rendern in `RenderServiceTests` · Prüfung: mit Lupe und Pfeiltasten einen Bereich um ein Symbol wählen, Enter, in Paint einfügen und Kanten bei 800 % vergleichen |
| P2: Alle Ausgabe-Knöpfe funktionieren; Esc speichert den Stand | erfüllt | `ViewModels\EditorViewModel.cs`, `Views\EditorWindow.xaml.cs` | Esc per `WM_KEYDOWN` im Tray-Lauf: „Bearbeitungsstand gespeichert“ |
| P3: Jedes Werkzeug erzeugt auswählbare, verschiebbare, skalierbare, löschbare Objekte | erfüllt | `ViewModels\Tools\` | `--test-annotate`: 12 Objekte, alle 11 Typen, 14 Schritte |
| P3: Verpixelte Stellen nicht rekonstruierbar | erfüllt | `Services\ImageEffects.cs` | `ImageEffectsTests.ComputeEffect_PixelatedRegionContainsNoOriginalPixels` |
| P3: Virtuelle Maus an Originalposition, verschiebbar, ausblendbar | erfüllt | `ViewModels\Tools\CursorTool.cs` | |
| P3: Rückgängig/Wiederholen ≥ 50 Schritte inkl. Bereich | erfüllt | `ViewModels\Undo\UndoStack.cs` | 200 Schritte |
| P4: Aufnahme von gestern öffnet genau wie gespeichert, alles änderbar | erfüllt | `Services\CaptureWorkflow.cs` (`OpenExisting`) | Rundreise-Tests, `--open` |
| P4: Nach Abstecken eines Monitors Fenster-Modus mit Zoom | nur manuell prüfbar | `Services\ScreenCoordinates.cs` (`IsSameLayout`) | Test für abgesteckten Monitor · Prüfung: Aufnahme mit zwei Monitoren, einen abstecken, aus der History bearbeiten → maximiertes Fenster, Strg+Mausrad zoomt |
| P4: History bei 5.000 Aufnahmen flüssig | erfüllt | `ViewModels\HistoryViewModel.cs` | 661 ms erster Block, 3,6 s alle |
| P4: Löschen verschiebt alle Dateien in den Papierkorb | erfüllt | `Services\StorageService.cs` | `--test-delete` |
| P5: Einstellungen ohne Neustart, Hotkey-Konflikte beim Speichern gemeldet | erfüllt | `App.xaml.cs` (`onSettingsChanged`), `ViewModels\HotkeyConflictChecker.cs` | Hotkeys werden bei Änderung neu registriert, alter Hook/Hotkey vorher freigegeben |
| P5: Autostart ein/aus | nur manuell prüfbar | `Services\AutostartService.cs` | Prüfung: siehe 3.5 |
| P5: Autostart mit Adminrechten ein/aus, Druck bei Admin-Fenster im Vordergrund | nur manuell prüfbar | `Services\ElevatedAutostartService.cs` | Prüfung: siehe 3.5; zusätzlich nach Deinstallation (ohne Adminrechte) prüfen, ob die Aufgabe entfernt ist – sonst erscheint ein Hinweis |

Nicht bewertet: die Ausbaustufen aus SPEC Abschnitt 8 (OCR, Fenster einrasten, gemischte Skalierung, Englisch);
„Klick ohne Ziehen rastet auf Fenster ein“ in 3.3 ist dort ausdrücklich als Ausbaustufe markiert.

## Zusammenfassung

| Status | Anzahl |
|---|---|
| erfüllt | 85 |
| teilweise | 1 |
| fehlt | 0 |
| nur manuell prüfbar | 16 |
| **gesamt** | **102** |
