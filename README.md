# ScreenKeeper

Screenshot-Tool für Windows 10 (ab 2004) und Windows 11 als Ersatz für Greenshot. C#, WPF, .NET 10, ohne NuGet-Abhängigkeiten.

**Kernidee:** Jede Aufnahme fotografiert **alle Monitore komplett**. Dieses Original-PNG wird nie verändert und dient als
Backup. Bereich, Zeichnungen und virtuelle Maus liegen getrennt davon in einer JSON-Datei daneben und bleiben dadurch
**jederzeit nachträglich änderbar**, auch Tage später aus der History.

## Funktionen

- **Hotkeys** (frei konfigurierbar): Druck = Aufnahme + Editor, Strg+Druck = nur speichern, Shift+Druck = History.
  Fällt `RegisterHotKey` aus (z. B. weil Greenshot läuft), greift ein Tastatur-Hook.
- **Vollbild-Editor** 1:1 über alle Monitore: Bereich aufziehen, 8 Anfasser, Lupe mit Pixelraster, Fadenkreuz,
  Koordinaten und Farbwert, Pfeiltasten bewegen um genau 1 Pixel (Shift: 10).
- **Zeichenwerkzeuge** als nachträglich änderbare Objekte: Rechteck, Ellipse, Linie, Pfeil, Freihand, Text, Textmarker,
  Verpixeln, Weichzeichnen, Nummern-Stempel, virtuelle Maus. Verpixeln und Weichzeichnen sind nicht destruktiv.
- **Rückgängig / Wiederholen** für alles, auch für den Bereich.
- **Ausgabe:** Bereich, aktueller Monitor oder ganzer Bildschirm in die Zwischenablage (Bitmap und PNG), als PNG speichern
  oder „Speichern unter“ als PNG/JPG.
- **History:** alle Aufnahmen nach Tagen gruppiert, Datumsfilter, Nachladen beim Scrollen, Bearbeiten, erneut kopieren,
  im Explorer anzeigen, Löschen in den Papierkorb. Aufnahmen bleiben auch bei geänderter Monitor-Anordnung bearbeitbar
  (Fenster-Modus mit Zoom).
- **Einstellungen:** Ablageordner, Dateinamen-Muster, Exportordner, Hotkeys mit Konfliktprüfung, Lupe, Standardfarben,
  Autostart, Aufbewahrungsdauer.

## Download

Fertige Builds liegen unter [Releases](https://github.com/boehla/ScreenKeeper/releases):

| Datei | Inhalt |
|---|---|
| `ScreenKeeper-<Version>-win-x64.zip` | Einzelne EXE, enthält die .NET-Laufzeit (keine Installation nötig) |
| `ScreenKeeper-<Version>-win-x64-framework-dependent.zip` | Kleiner, benötigt die installierte [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

Entpacken, `ScreenKeeper.exe` starten. Das Programm läuft aus jedem Ordner und schreibt nichts neben die EXE.
ScreenKeeper sucht einmal täglich im Hintergrund nach einer neuen Version und zeigt sie unaufdringlich in der History,
im Tray-Menü und in den Einstellungen an; ein Klick installiert sie (ZIP laden, EXE ersetzen, Neustart). Einstellungen
liegen in `%APPDATA%\ScreenKeeper\settings.json`, Aufnahmen im Bilder-Ordner unter `ScreenKeeper` (konfigurierbar).

## Selbst bauen

Voraussetzung: .NET SDK 10.

```
dotnet build ScreenKeeper\ScreenKeeper.csproj -c Release
```

Die EXE liegt danach unter `ScreenKeeper\bin\Release\net10.0-windows10.0.19041.0\ScreenKeeper.exe`.

Tests:

```
dotnet test ScreenKeeper.Tests\ScreenKeeper.Tests.csproj
```

## Dokumentation

| Datei | Inhalt |
|---|---|
| [ScreenKeeper/README.md](ScreenKeeper/README.md) | Ausführliche Bedienung: Hotkeys, Editor, Werkzeuge, Tastenkürzel, History, Einstellungen |
| [ScreenKeeper/CHANGELOG.md](ScreenKeeper/CHANGELOG.md) | Änderungen je Version |
| [SPEC.md](SPEC.md) | Fachliche Spezifikation |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Technische Festlegungen, Datenformat, Code-Stil |
| [TESTS.md](TESTS.md) | Build, Unit-Tests und Headless-Testschalter |
| [ABNAHME.md](ABNAHME.md) | Abgleich der Umsetzung mit der Spezifikation |

## Datenformat

Neben jedem Original-PNG liegt eine JSON-Datei mit Monitoren, Fenstern, Mauszeiger, Bereich und Zeichnungen, alle
Koordinaten in Bildpixeln. Details in [SPEC.md](SPEC.md), Abschnitt 4.

## Releases

Ein Push auf `main` mit geänderter `<Version>` in `ScreenKeeper/ScreenKeeper.csproj` baut, testet und veröffentlicht
automatisch ein GitHub-Release `v<Version>` mit beiden ZIP-Dateien und den Notizen aus dem CHANGELOG
(siehe [.github/workflows/release.yml](.github/workflows/release.yml)).

## Offene Punkte

- **History- und Einstellungsfenster überarbeiten:** Beide Fenster sind funktional, aber noch im WPF-Standardlook.
  Geplant ist eine Oberfläche im Stil der Editor-Werkzeugleiste (schwebend, reduziert, klare Symbole), mit der sich das
  Zeichnen bereits gut anfühlt.
- Schriftart beim Text-Werkzeug ist fest Segoe UI.
- Ausbaustufen laut [SPEC.md](SPEC.md) Abschnitt 8: OCR, Einrasten auf Fenster, gemischte DPI-Skalierung, englische Oberfläche.

## Lizenz

[MIT](LICENSE). Nutzung, Änderung und Weitergabe sind frei, auch kommerziell.
