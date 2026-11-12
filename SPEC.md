# ScreenKeeper – Spezifikation und Umsetzungsplan

Stand: 09.11.2026 · Version 1.0 des Plans

Dieses Dokument ist eigenständig: Es beschreibt vollständig, was gebaut werden soll, ohne Bezug auf einen bestimmten Rechner oder ein bestimmtes Repository. Die Integration in die Tool-Landschaft des Auftraggebers erfolgt nach der Übergabe durch den Auftraggeber (siehe Abschnitt 9).

---

## 1. Ziel

Ein Screenshot-Tool für Windows als Ersatz für Greenshot. Kernidee:

> Jede Aufnahme fotografiert **alle Monitore komplett**. Dieses Original wird **nie verändert** und dient als Backup. Bereich, Zeichnungen und virtuelle Maus werden **getrennt davon in einer JSON-Datei** gespeichert und bleiben dadurch **jederzeit nachträglich änderbar** – auch Tage später aus der History.

---

## 2. Rahmenbedingungen

| Punkt | Vorgabe |
|---|---|
| Plattform | Nur Windows 10 (ab 2004) / Windows 11, x64 |
| Sprache / UI | C#, **WPF**, MVVM-Aufbau (Views / ViewModels / Models / Services) |
| Framework | **.NET 10**, `TargetFramework` = `net10.0-windows10.0.19041.0` (nötig für die spätere OCR über `Windows.Media.Ocr`) |
| Abhängigkeiten | **Keine NuGet-Pakete** außer bei zwingendem Bedarf – vorher mit dem Auftraggeber abstimmen. Win32-Aufrufe per P/Invoke. |
| DPI | Manifest mit `PerMonitorV2`. **Version 1 unterstützt nur gleiche Skalierung auf allen Monitoren.** Das Datenmodell arbeitet aber durchgehend in Bildpixeln, damit gemischte Skalierung später ergänzt werden kann (Abschnitt 8). |
| Oberflächensprache | Deutsch (Texte zentral in einer Ressourcendatei, damit Englisch später ergänzt werden kann) |
| Einstellungen | Je Benutzer in `%APPDATA%\ScreenKeeper\settings.json` – **keine absoluten Pfade im Code**, keine Registry außer Autostart |
| Instanz | Nur eine Instanz pro Benutzer (Mutex); ein zweiter Start bringt das Hauptfenster nach vorne |
| Encoding | Quelldateien UTF-8 mit BOM, Zeilenende CRLF |
| Tests | Manuelle Abnahme je Phase (Abschnitt 7); automatisierte Tests sind optional für Datenmodell und JSON-Serialisierung |

---

## 3. Funktionen

### 3.1 Tray und Hotkeys

- Läuft im Hintergrund mit Tray-Symbol. Kontextmenü: *Aufnahme*, *Verzögerte Aufnahme* (ab 0.9), *History öffnen*, *Einstellungen*, *Beenden*. Doppelklick öffnet die History.
- Hotkeys sind **frei konfigurierbar**. Standardbelegung:

| Hotkey | Aktion |
|---|---|
| **Druck** | Aufnahme + Bearbeiten-Modus öffnen |
| Strg+Druck | Aufnahme nur speichern (kein Editor, kurze Tray-Benachrichtigung) |
| Shift+Druck | History öffnen |

- Registrierung über `RegisterHotKey`. Ist die Druck-Taste dort nicht registrierbar, wird sie über einen Low-Level-Tastatur-Hook (`WH_KEYBOARD_LL`) abgefangen.
- Remotedesktop (ab 0.9): Ist ein Fenster mit einer Klasse aus `remoteSessionWindowClasses` (Standard `TscShellContainerClass`) im Vordergrund, laufen alle eingestellten Hotkeys über den Low-Level-Hook; er wird beim Aktivieren des Fensters (per `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`) sofort und nach 250 ms erneut gesetzt, damit er vor dem Hook von mstsc liegt. Nur die eingestellten Kombinationen werden geschluckt. Außerhalb davon bleibt alles wie bisher (WM_HOTKEY). Abschaltbar (`captureHotkeysInRemoteSessions`). Optionaler Durchreich-Hotkey (`hotkeyRemotePrintScreen`): schickt dort per `SendInput` ein reines Druck an die Sitzung.
- Verzögerte Aufnahme (ab 0.9, Hotkey `hotkeyCaptureDelayed`, Tray-Menü): Countdown (`captureDelaySeconds`, 1–60) mit Overlay (fokuslos, von der Aufnahme ausgenommen) oder vorher Abfrage (`captureDelayAskEachTime`); danach Editor oder nur speichern (`delayedCaptureAction`). Esc/Tray-Klick/andere Aufnahme bricht ab, der Hotkey erneut löst sofort aus.
- Erkennen und **nur als Hinweis anzeigen** (nicht selbst ändern):
  - Windows-Einstellung „Druck-Taste öffnet Bildschirmausschnitt“ (`HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled = 1`)
  - laufendes Greenshot oder ein anderes Programm, das den Hotkey belegt.

### 3.2 Aufnahme und Backup

1. Den gesamten virtuellen Bildschirm (alle Monitore) **ohne Mauszeiger** in einem Durchgang aufnehmen.
2. Zusätzlich erfassen:
   - Monitor-Grenzen (Name, Bereich, primär ja/nein, Skalierung)
   - **Mauszeiger**: Bild (inkl. Transparenz), Position, Hotspot, sichtbar ja/nein (`GetCursorInfo`, `GetIconInfo`)
   - **sichtbare Top-Level-Fenster** mit Bereich, Titel, Prozessname und Z-Reihenfolge (`EnumWindows`, DWM-Rahmen über `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`) – für das spätere Einrasten auf Fenster
3. **Sofort** speichern (vor dem Öffnen des Editors), damit das Backup auch bei einem Absturz vorhanden ist:

```
<Ablageordner>\<JJJJ>\<MM>\
   2026-11-09_14-32-05.png        ← Original, verlustfrei, wird nie überschrieben
   2026-11-09_14-32-05.json       ← Metadaten und Bearbeitungsstand (Abschnitt 4)
   2026-11-09_14-32-05.thumb.jpg  ← Vorschaubild für die History (max. 320 px breit)
```

- Standard-Ablageordner: **Bilder-Ordner des Benutzers** (Known Folder *Pictures*) `\ScreenKeeper`. Konfigurierbar.
- Dateinamen-Muster konfigurierbar mit Platzhaltern `{yyyy} {MM} {dd} {HH} {mm} {ss}`; bei Namensgleichheit Suffix `_2`, `_3` …
- Die Aufnahme muss sich „sofort“ anfühlen: Ziel **< 300 ms** vom Tastendruck bis zum sichtbaren Editor bei 2 × Full-HD. Das PNG darf im Hintergrund geschrieben werden, solange der Editor schon das Bild im Speicher anzeigt.

### 3.3 Bearbeiten-Modus (Vollbild)

**Darstellung**
- Ein rahmenloses, oberstes Fenster über den **gesamten virtuellen Bildschirm** zeigt das eingefrorene Bild 1:1. (Bei gleicher Skalierung funktioniert ein einziges Fenster zuverlässig; für gemischte Skalierung siehe Abschnitt 8.)
- Außerhalb des gewählten Bereichs wird das Bild abgedunkelt (ca. 50 %).
- Eine Werkzeugleiste schwebt am Bereich (unter- oder oberhalb, je nach Platz); vor der ersten Bereichswahl am oberen Rand des primären Monitors.

**Bereich wählen**
- Bereich mit der Maus aufziehen. Danach jederzeit änderbar: 8 Anfasser, Verschieben per Ziehen im Bereich, neues Aufziehen außerhalb.
- **Lupe** (wie Greenshot), sichtbar während des Aufziehens und beim Ziehen der Anfasser:
  - Vergrößerung standardmäßig 8-fach (einstellbar 4–16), Pixelraster, Fadenkreuz
  - Anzeige: Position (x, y), Bereichsgröße (B × H), Farbwert des Pixels unter dem Fadenkreuz (Hex)
  - Lupe weicht dem Cursor aus, damit sie nie den Zielpunkt verdeckt, und bleibt innerhalb des Monitors
  - Ab 0.9 direkt im Editor verstellbar: Mausrad (Fenster-Modus Alt+Mausrad) = Zoom in ganzen Schritten, +/- = Größe in
    10er-Schritten (80–400 px); gespeichert gesammelt nach 1 s Ruhe bzw. beim Schließen. Optionale Tastenhilfe als
    zweite Zeile unter der Lupe (Einstellung, Standard an).
- **Letzten Bereich** (ab 0.9, Knopf + Strg+L): zuletzt gewählten Bereich in Bildschirmkoordinaten merken (beim
  Schließen, nur bei gleicher Monitor-Anordnung und geändertem Bereich, nicht beim Verwerfen) und auf eine neue Aufnahme
  übernehmen (ein Rückgängig-Schritt, auf das Bild begrenzt).
- **Tastatur:**
  - Pfeiltasten bewegen den Cursor (bzw. die aktive Kante) um **1 Pixel**, Shift+Pfeiltaste um 10 Pixel
  - Leertaste / Enter setzt den Startpunkt bzw. Endpunkt, wenn der Bereich per Tastatur gewählt wird
- Ausbaustufe (Abschnitt 8): Klick ohne Ziehen rastet auf das Fenster unter dem Cursor ein (aus den gespeicherten Fensterdaten).

**Zeichenwerkzeuge** (alle als einzelne, nachträglich auswählbare Objekte)

| Werkzeug | Eigenschaften |
|---|---|
| Auswahl / Verschieben | Objekt anklicken, verschieben, Größe ändern, Entf löscht |
| Rechteck, Ellipse | Rahmenfarbe, Strichstärke, Füllung optional |
| Linie, Pfeil | Farbe, Strichstärke; Shift = 45°-Raster |
| Freihand | Farbe, Strichstärke, Glättung |
| Text | Klick = freier Text (wächst mit dem Inhalt); Aufziehen = festes Textfeld wie ein Rechteck (verschieben, an allen Anfassern in Breite/Höhe ändern, Text wird umbrochen und am Feldrand abgeschnitten); Schriftgröße fest (Liste oder eigene Eingabe 6–400 px) oder „Auto“ (größte Schrift, bei der der Text ins Feld passt); Ausrichtung 3×3 (horizontal Zeilen, vertikal Lage im Feld); Farbe, Hintergrund optional; Doppelklick zum Bearbeiten (ab 0.10) |
| Highlight (Textmarker) | Rechteck im Modus „Multiplizieren“ (Text darunter bleibt dunkel, Weiß wird zur Markerfarbe), Farbe (Standard Gelb) |
| Verpixeln | Rechteck, Blockgröße einstellbar |
| Weichzeichnen | Rechteck, Stärke einstellbar |
| Nummern-Stempel | fortlaufend 1, 2, 3 …, Farbe, Größe |
| **Virtuelle Maus** | siehe unten |

- Farbe und Strichstärke über die Werkzeugleiste; zuletzt verwendete Werte werden gemerkt (beim Text auch
  Auto/feste Schriftgröße und Ausrichtung, auch wenn sie an einem vorhandenen Text geändert wurden).
- **Rückgängig / Wiederholen** (Strg+Z / Strg+Y) für alle Änderungen inkl. Bereich und Maus.
- Verpixeln und Weichzeichnen arbeiten **nicht destruktiv**: Sie werden bei Anzeige und Export aus dem Original berechnet.
- Zeichnungen dürfen außerhalb des Bereichs liegen; sie bleiben gespeichert und werden erst beim Export abgeschnitten.

**Virtuelle Maus**
- Der echte Mauszeiger ist im Original-PNG nie enthalten.
- Der aufgenommene Zeiger wird als Objekt an seiner Originalposition angezeigt (standardmäßig sichtbar, Einstellung).
- Er lässt sich verschieben, ausblenden, löschen und erneut einfügen, auch mehrfach. Auswahl des Zeigers: *aufgenommener Zeiger*, Pfeil, Hand, Textcursor (Standardzeiger von Windows).

**Knöpfe zum Beenden** (Werkzeugleiste + Tastatur)

| Knopf | Taste | Ergebnis |
|---|---|---|
| Bereich → Zwischenablage | Enter, Strg+C | gerendertes PNG des Bereichs |
| Bereich speichern | Strg+S | PNG im Exportordner (Standard: `<Ablageordner>\Export`) |
| Bereich speichern unter … | Strg+Shift+S | Dialog mit freiem Pfad, PNG oder JPG |
| Ganzer Bildschirm → Zwischenablage | Strg+Shift+C | alle Monitore inkl. Zeichnungen und Maus |
| Aktueller Monitor → Zwischenablage | – | Monitor, auf dem sich die Maus befindet |
| Abbrechen | Esc | kein Export |

- **Bei jedem Beenden, auch bei Esc,** wird der Bearbeitungsstand in die JSON-Datei geschrieben und das Vorschaubild aktualisiert.
- Ohne gewählten Bereich sind die Bereich-Knöpfe deaktiviert.
- Zwischenablage: Bild als Bitmap **und** als PNG-Format einstellen (damit Transparenz und Qualität in Outlook, Teams usw. passen).

### 3.4 History und nachträgliches Bearbeiten

- Hauptfenster mit Vorschaubildern der Aufnahmen, neueste zuerst, nach Tagen gruppiert; Datumsfilter; Nachladen beim Scrollen (Ordner können tausende Dateien enthalten).
- Anzeige je Eintrag: Vorschaubild, Uhrzeit, Kennzeichen „bearbeitet“ (Bereich oder Zeichnungen vorhanden).
- Aktionen: **Bearbeiten** (Doppelklick), erneut in Zwischenablage, im Explorer anzeigen, löschen (**in den Papierkorb**, alle zugehörigen Dateien), **Kommentar bearbeiten** (ab 0.9).
- **Kommentar** (ab 0.9): freier, mehrzeiliger Text je Aufnahme, im Editor (Knopf in der Werkzeugleiste, ein Rückgängig-Schritt) und in der History bearbeitbar, in der History angezeigt und von der Textsuche erfasst.
- **Bearbeiten öffnet denselben Editor** wie bei einer neuen Aufnahme, mit allen gespeicherten Daten:
  - Ist die aktuelle Monitor-Anordnung gleich der aufgenommenen → Vollbild 1:1 über die Monitore.
  - Sonst → maximiertes Fenster mit Zoom (Strg+Mausrad) und Verschieben (Leertaste+Ziehen bzw. mittlere Maustaste). Alle Funktionen gleich.
- Aufnahmen ohne JSON (z. B. beschädigt oder von Hand abgelegte PNGs) werden trotzdem angezeigt und bekommen beim ersten Bearbeiten eine neue JSON.
- Optional: automatisches Löschen von Aufnahmen älter als X Tage (Standard: **aus**), immer in den Papierkorb.

### 3.5 Einstellungen

Ablageordner · Dateinamen-Muster · Exportordner · Hotkeys (mit Konfliktprüfung) · Lupe (Zoom, Größe, Tastenhilfe) · Standardfarben und Strichstärke · Mauszeiger standardmäßig sichtbar · Autostart mit Windows (`HKCU\...\Run`) · Aufbewahrungsdauer · Tray-Benachrichtigung bei „nur speichern“ ein/aus · Hotkeys in Remotedesktop-Sitzungen abfangen · verzögerte Aufnahme (Sekunden, jedes Mal fragen, danach Editor oder nur speichern).

---

## 4. Datenformat (JSON neben dem PNG)

- Alle Koordinaten in **Bildpixeln des Original-PNGs** (0,0 = linke obere Ecke des PNGs), nicht in Bildschirmkoordinaten.
- `formatVersion` erlaubt spätere Erweiterungen; ältere Versionen müssen immer lesbar bleiben.
- Schreiben atomar (temporäre Datei + Umbenennen), damit bei Absturz keine halbe Datei entsteht.

```json
{
  "formatVersion": 1,
  "capturedAt": "2026-11-09T14:32:05+01:00",
  "image": "2026-11-09_14-32-05.png",
  "virtualScreen": { "x": -1920, "y": 0, "width": 3840, "height": 1080 },
  "monitors": [
    { "device": "\\\\.\\DISPLAY1", "bounds": { "x": 1920, "y": 0, "width": 1920, "height": 1080 }, "primary": true,  "scale": 1.0 },
    { "device": "\\\\.\\DISPLAY2", "bounds": { "x": 0,    "y": 0, "width": 1920, "height": 1080 }, "primary": false, "scale": 1.0 }
  ],
  "windows": [
    { "title": "Rechner", "process": "CalculatorApp", "bounds": { "x": 100, "y": 80, "width": 400, "height": 600 }, "z": 0 }
  ],
  "cursor": { "png": "<base64>", "x": 512, "y": 300, "hotspotX": 0, "hotspotY": 0, "capturedVisible": true },
  "selection": { "x": 80, "y": 60, "width": 640, "height": 480 },
  "annotations": [
    { "id": "3f2a…", "type": "arrow", "points": [[100,100],[220,180]], "stroke": "#FFE53935", "thickness": 4 },
    { "id": "8c1d…", "type": "pixelate", "rect": { "x": 300, "y": 200, "width": 120, "height": 30 }, "blockSize": 10 },
    { "id": "a7b0…", "type": "cursor", "x": 540, "y": 320, "kind": "captured" }
  ],
  "ocrText": null
}
```

- `monitors[].bounds` und `windows[].bounds` sind **ebenfalls in Bildpixeln** (umgerechnet über `virtualScreen`); `virtualScreen` hält die ursprünglichen Bildschirmkoordinaten fest.
- `annotations` ist eine polymorphe Liste (`type`-Diskriminator) in Zeichenreihenfolge.
- Optional (ab 0.9, `formatVersion` bleibt 1): `"comment": "…"` – Kommentar zur Aufnahme; fehlt das Feld oder ist es leer, gibt es keinen Kommentar (leer wird nicht geschrieben).
- Im Ablageordner liegt eine automatisch gepflegte `AGENTS.md` (Inhalt im Programm eingebettet, mit Programmversion), die Ordnerstruktur, Dateinamen und dieses Format für KI-Agenten und Skripte beschreibt. Sie wird nur geschrieben, wenn sie fehlt oder abweicht; History, Aufräumen und Export ignorieren sie.

---

## 5. Architektur (Vorschlag)

```
ScreenKeeper\
   ScreenKeeper.csproj, app.manifest, App.xaml
   Models\        CaptureDocument, MonitorInfo, WindowInfo, CursorInfo, Annotation (+ Unterklassen)
   Services\      HotkeyService, CaptureService, StorageService (Ablage, JSON, Thumbnails),
                  RenderService (Export: Bereich/Monitor/Gesamt), ClipboardService,
                  SettingsService, AutostartService, Native\ (P/Invoke)
   ViewModels\    EditorViewModel, HistoryViewModel, SettingsViewModel, Undo\ (Command-Stack)
   Views\         EditorWindow (Vollbild/Fenster-Modus), Magnifier, Toolbar, HistoryWindow, SettingsWindow
   Resources\     Strings.resx, Icons
   README.md, CHANGELOG.md
```

- **Ein Render-Pfad** für Anzeige und Export (`RenderService`), damit das Ergebnis in der Zwischenablage exakt dem Editor entspricht.
- Editor arbeitet ausschließlich auf `CaptureDocument`; Undo/Redo über Befehle auf diesem Modell.
- Lupe liest direkt aus dem Original-Bitmap im Speicher (nicht vom Bildschirm).

---

## 6. Phasen und Aufwand

| Phase | Inhalt | Aufwand |
|---|---|---|
| 0 | Projekt anlegen, Manifest (DPI), MVVM-Grundgerüst, Einzelinstanz, Tray, README/CHANGELOG | ½ Tag |
| 1 | Hotkeys inkl. Druck (+ Hook-Fallback, Hinweise), Aufnahme aller Monitore, Maus- und Fensterdaten, Speichern PNG + JSON + Vorschaubild | 1½–2 Tage |
| 2 | Vollbild-Editor, Bereichswahl mit Anfassern, **Lupe mit Pfeiltasten**, Ausgabe-Knöpfe, Speichern des Bearbeitungsstands → **ab hier im Alltag als Greenshot-Ersatz nutzbar** | 3–4 Tage |
| 3 | Alle Zeichenwerkzeuge, virtuelle Maus, Rückgängig/Wiederholen | 5–7 Tage |
| 4 | History-Fenster, nachträgliches Bearbeiten, Fenster-Modus mit Zoom bei geänderter Monitor-Anordnung | 2–3 Tage |
| 5 | Einstellungsmaske, Hotkey-Konfiguration mit Konfliktprüfung, Autostart, Aufbewahrung | 1–2 Tage |
| 6 | Feinschliff: Leistung, 4K, Fokus- und Vordergrundprobleme, Sonderfälle | 2–3 Tage |

**Gesamt etwa 15–21 Arbeitstage.** Jede Phase endet mit einem lauffähigen Stand und der Abnahme nach Abschnitt 7.

---

## 7. Abnahmekriterien je Phase

**Phase 1**
- [ ] Druck-Taste löst die Aufnahme aus, auch wenn Explorer, Browser oder eine Vollbild-Anwendung im Vordergrund ist.
- [ ] Ist die Snipping-Tool-Einstellung aktiv oder Greenshot gestartet, erscheint ein verständlicher Hinweis.
- [ ] PNG enthält alle Monitore in korrekter Anordnung, ohne Mauszeiger; Monitore links/oberhalb des primären (negative Koordinaten) sind korrekt.
- [ ] JSON und Vorschaubild liegen daneben; JSON ist gültig und enthält Monitore, Fenster und Maus.

**Phase 2**
- [ ] Editor ist < 300 ms nach Tastendruck sichtbar (2 × Full-HD).
- [ ] Lupe zeigt Raster, Fadenkreuz, Koordinaten, Größe und Farbwert; Pfeiltasten bewegen um genau 1 Pixel, Shift um 10.
- [ ] Ein per Lupe auf 1 Pixel genau gewählter Bereich ergibt in der Zwischenablage exakt diese Pixel (Prüfung in Paint).
- [ ] Alle Ausgabe-Knöpfe funktionieren; Esc speichert den Stand trotzdem in der JSON.

**Phase 3**
- [ ] Jedes Werkzeug erzeugt ein Objekt, das sich auswählen, verschieben, in der Größe ändern und löschen lässt.
- [ ] Verpixelte Stellen sind im Export nicht rekonstruierbar (Original-Pixel sind im Export nicht enthalten).
- [ ] Virtuelle Maus erscheint an der Originalposition, ist verschiebbar und ausblendbar.
- [ ] Rückgängig/Wiederholen funktioniert über mindestens 50 Schritte, inkl. Bereichsänderungen.

**Phase 4**
- [ ] Eine Aufnahme von gestern öffnet mit Bereich, Zeichnungen und Maus genau wie gespeichert; alles bleibt änderbar.
- [ ] Nach Abstecken eines Monitors öffnet dieselbe Aufnahme im Fenster-Modus mit Zoom.
- [ ] History bleibt bei 5.000 Aufnahmen flüssig bedienbar.
- [ ] Löschen verschiebt alle zugehörigen Dateien in den Papierkorb.

**Phase 5**
- [ ] Alle Einstellungen wirken ohne Neustart; Hotkey-Konflikte werden beim Speichern gemeldet.
- [ ] Autostart ein/aus funktioniert.

---

## 8. Spätere Ausbaustufen (nicht in Version 1)

| Ausbau | Beschreibung | Aufwand |
|---|---|---|
| OCR | „Text aus Bereich kopieren“ über `Windows.Media.Ocr` (offline, Deutsch/Englisch); Ergebnis in `ocrText`, durchsuchbar in der History | 1–2 Tage |
| Fenster einrasten | Klick ohne Ziehen wählt das Fenster unter dem Cursor (Daten aus `windows`) | 1 Tag |
| Gemischte Skalierung | Ein Editor-Fenster **pro Monitor** mit gemeinsamem Modell statt eines Fensters über alles | 2–3 Tage |
| Englische Oberfläche | Zweite Ressourcendatei | ½ Tag |

**Nicht vorgesehen:** Video-/GIF-Aufnahme, Upload ins Internet, Cloud-Ablage.

---

## 9. Übergabe und Integration

Geliefert wird:
- Quellcode als **ein Projektordner** mit einer `.csproj` (ohne eigene Solution-Abhängigkeiten), baubar mit `dotnet build` ohne weitere Schritte
- keine absoluten Pfade, keine Abhängigkeit von einem bestimmten Rechner, Laufwerk oder Benutzer
- Versionsnummer in der `.csproj` (`<Version>`), `CHANGELOG.md` je Phase
- `README.md` mit Kurzbeschreibung, Build-Befehl und Bedienung (Hotkeys, Knöpfe)
- Programmsymbol als `.ico` im Projektordner

Die Integration in das Tool-Repository des Auftraggebers (Solution, Veröffentlichung, Starter) übernimmt der Auftraggeber nach der Übergabe. Deshalb gilt: Das Programm muss **aus einem beliebigen lokalen Ordner** lauffähig sein und darf nichts neben die EXE schreiben – alle Daten nur in `%APPDATA%\ScreenKeeper\` und im konfigurierten Ablageordner.
