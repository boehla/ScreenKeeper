# ScreenKeeper

Screenshot-Tool für Windows 10 (ab 2004) und Windows 11 als Ersatz für Greenshot.

Jede Aufnahme fotografiert **alle Monitore komplett**. Dieses Original-PNG wird nie verändert und dient als Backup.
Bereich, Zeichnungen und virtuelle Maus werden getrennt davon in einer JSON-Datei gespeichert und bleiben dadurch
jederzeit nachträglich änderbar.

## Build

Voraussetzung: .NET SDK 10.

```
dotnet build ScreenKeeper\ScreenKeeper.csproj -c Release
```

Die EXE liegt danach unter `ScreenKeeper\bin\Release\net10.0-windows10.0.19041.0\ScreenKeeper.exe`.
Das Programm läuft aus jedem beliebigen Ordner und schreibt nichts neben die EXE.

## Bedienung

ScreenKeeper läuft im Hintergrund mit einem Symbol im Infobereich der Taskleiste.

- **Rechtsklick** auf das Symbol: *Aufnahme*, *History öffnen*, *Einstellungen*, *Beenden* – und nur wenn eine neue
  Version bereitsteht *Update auf x installieren*
- **Doppelklick** auf das Symbol: History öffnen
- Ein zweiter Programmstart öffnet die History der laufenden Instanz.

### Hotkeys (Standard)

| Hotkey | Aktion |
|---|---|
| Druck | Aufnahme + Bearbeiten |
| Strg+Druck | Aufnahme nur speichern (kurze Benachrichtigung) |
| Shift+Druck | History öffnen |

Ist der Editor bereits offen, holen Druck und Strg+Druck ihn nur nach vorne (keine zweite Aufnahme).
Ist die Druck-Taste bereits von einem anderen Programm belegt, fängt ScreenKeeper sie über einen Tastatur-Hook ab.
Beim Start erscheint ein Hinweis, wenn Windows die Druck-Taste für das Bildschirmausschnitt-Tool verwendet oder
Greenshot (bzw. ShareX, Lightshot) läuft. ScreenKeeper ändert diese Einstellungen nicht selbst.
Alle Hotkeys lassen sich in den Einstellungen ändern oder abschalten.

### History

Die History ist das Hauptfenster. Sie zeigt alle Aufnahmen, neueste zuerst, nach Tagen gruppiert (Heute, Gestern,
Datum) – mit Vorschaubild, Uhrzeit, darunter dem **Programm im Vordergrund** bei der Aufnahme (Prozessname und gekürzter
Fenstertitel, der Tooltip zeigt ihn ganz), dem Kennzeichen **bearbeitet**, wenn ein Bereich oder Zeichnungen gespeichert
sind, und **permanent** (Stern rechts in der Zeile, gefüllt = permanent). Weitere Einträge werden beim Scrollen nachgeladen, auch Tausende Aufnahmen bleiben flüssig. Schließen versteckt
das Fenster nur; ScreenKeeper läuft im Infobereich weiter.

| Aktion | Bedienung |
|---|---|
| Bearbeiten | Doppelklick, Enter, Knopf oder Kontextmenü |
| In Zwischenablage | Strg+C – der gespeicherte Bereich (ohne Bereich das ganze Bild) mit Zeichnungen und Maus |
| Kopie anlegen | Strg+D, Knopf oder Kontextmenü – dieselbe Aufnahme noch einmal mit eigenem Bereich und eigenen Zeichnungen |
| Permanent | P, Stern in der Zeile, Knopf oder Kontextmenü – umschalten; permanente Aufnahmen löscht das Aufräumen nie |
| Im Explorer anzeigen | Knopf oder Kontextmenü |
| Löschen | Entf (mit Rückfrage, Mehrfachauswahl möglich) – Bild, JSON und Vorschaubild kommen in den Papierkorb |
| Aktualisieren | F5 |
| Ordner öffnen | öffnet den Ablageordner |

- **Filter:** Von/Bis-Datum, Schnellwahl *Heute*, *7 Tage*, *30 Tage*, *Alle*, zusätzlich der Schalter *Permanent*
  (nur permanente Aufnahmen) und die Suche – sie findet Text im Dateinamen, im Programmnamen und im Fenstertitel des
  Vordergrundprogramms. Dafür werden die JSON-Dateien im Hintergrund einmal gelesen und danach im Speicher gehalten.
- **Kopie anlegen** legt PNG, JSON und Vorschaubild unter dem nächsten freien Namen (`…_2`, `…_3`) im selben Ordner an.
  Die Kopie übernimmt Bereich und Zeichnungen (mit neuen Kennungen), ist aber nicht permanent; sie erscheint direkt über
  dem Original und ist ausgewählt. So lässt sich dieselbe Aufnahme mehrfach unterschiedlich bearbeiten.
- **Bearbeiten** öffnet denselben Editor wie bei einer neuen Aufnahme, mit Bereich, Zeichnungen und Maus wie
  gespeichert. Passt die Monitor-Anordnung nicht mehr, öffnet er im Fenster-Modus (siehe unten).
- PNGs ohne JSON (z. B. von Hand in den Ablageordner gelegt) erscheinen ebenfalls (*nur Bild*); beim ersten Bearbeiten
  wird die JSON-Datei angelegt.

### Einstellungen

Tray-Menü → *Einstellungen*. Alle Änderungen wirken sofort nach **OK** bzw. **Übernehmen**, ohne Neustart.

- **Allgemein:** Ablageordner (leer = `Bilder\ScreenKeeper`), Dateinamen-Muster mit den Platzhaltern
  `{yyyy} {MM} {dd} {HH} {mm} {ss}` und Vorschau, Exportordner (leer = `<Ablageordner>\Export`),
  Aufbewahrung, Autostart, Benachrichtigung bei „nur speichern“, Updates.
- **Hotkeys:** Feld anklicken und die Tastenkombination drücken (Esc bricht ab, Entf schaltet den Hotkey aus).
  Beim Speichern wird geprüft, ob zwei Hotkeys gleich sind (Fehler) und ob ein anderes Programm die Kombination belegt
  (Hinweis; bei der Druck-Taste übernimmt dann der Tastatur-Hook). Die Seite zeigt außerdem, wie die Druck-Taste
  gerade abgefangen wird (über RegisterHotKey, über den Tastatur-Hook oder nicht verfügbar).
- **Editor:** Lupe (Vergrößerung 4–16, Größe), Standardfarbe, Textmarkerfarbe, Füllfarbe, Strichstärke und die übrigen
  Standardwerte der Werkzeuge, aufgenommenen Mauszeiger standardmäßig anzeigen.

**Autostart:** Die Einstellung *Mit Windows starten* trägt ScreenKeeper unter
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` ein bzw. entfernt den Eintrag wieder.

**Aufbewahrung (Aufräumen):** „Aufnahmen nach X Tagen in den Papierkorb verschieben, außer permanente“. ScreenKeeper
verschiebt Aufnahmen, die älter als so viele Tage sind, beim Start und danach einmal täglich in den Papierkorb (Bild,
JSON und Vorschaubild) und schreibt die Anzahl ins Protokoll. **Permanent** markierte Aufnahmen bleiben immer erhalten,
ebenso Aufnahmen, deren JSON nicht lesbar ist; Aufnahmen ganz ohne JSON zählen als nicht permanent. Standard sind
30 Tage, 0 schaltet das Aufräumen aus. Eine vorhandene Einstellungsdatei ohne diesen Wert bekommt den Standard; wer
bisher ausdrücklich 0 eingestellt hatte, behält 0.

**Updates:** Mit *Einmal täglich im Hintergrund nach Updates suchen* (Standard: an) fragt ScreenKeeper 30 s nach dem
Start und danach höchstens einmal je 24 Stunden bei GitHub nach der neuesten Version (Zeitpunkt in
`%APPDATA%\ScreenKeeper\update.json`). Es erscheinen keine Dialoge und keine Benachrichtigungen: Gibt es eine neue
Version, steht unten in der History „Version x verfügbar – installieren“, im Tray-Menü *Update auf x installieren*,
im Tooltip „· Update verfügbar“ und auf der Einstellungsseite das Ergebnis (dort prüft *Jetzt prüfen* sofort).
Ein Klick auf *installieren* lädt das passende ZIP (mit bzw. ohne .NET-Laufzeit, je nachdem welche Variante läuft) nach
`%LOCALAPPDATA%\ScreenKeeper\update\<Version>\` (Fortschritt in der Statuszeile), entpackt es, speichert einen offenen
Editor, beendet ScreenKeeper, ersetzt die EXE und startet neu. Fehler stehen nur im Statustext. Ein selbst gebauter
Entwicklungsstand (aus `bin\`) öffnet stattdessen die Release-Seite im Browser.

### Bearbeiten

Nach der Aufnahme wird das eingefrorene Bild 1:1 über alle Monitore angezeigt.

**Bereich wählen**

- Mit der Maus aufziehen. Danach jederzeit änderbar: an den 8 Anfassern (oder an den Kanten) ziehen, im Bereich
  ziehen zum Verschieben, außerhalb neu aufziehen. Außerhalb des Bereichs ist das Bild abgedunkelt; am Bereich steht
  die Größe in Pixeln.
- Die **Lupe** erscheint vor der ersten Auswahl, beim Aufziehen und beim Ziehen der Anfasser. Sie zeigt die Pixel
  vergrößert mit Raster und Fadenkreuz, darunter Position, Bereichsgröße und Farbwert des Pixels unter dem Fadenkreuz.
- **Tastatur:** Pfeiltasten bewegen den Mauszeiger (bzw. beim Ziehen die Kante) um genau 1 Pixel, mit Shift um 10.
  **Leertaste** oder **Enter** setzt den Startpunkt, ein zweiter Druck den Endpunkt – beide Pixel gehören zum Bereich.
- **Strg+Z** / **Strg+Y**: Rückgängig / Wiederholen.

**Zeichnen**

Die Werkzeugleiste enthält links die Werkzeuge, daneben die Optionen des aktiven Werkzeugs bzw. des ausgewählten
Objekts. Alle Zeichnungen bleiben einzelne Objekte und lassen sich jederzeit auswählen, verschieben, in der Größe
ändern und löschen – auch Tage später aus der gespeicherten Aufnahme. Das Original-PNG wird nie verändert.

| Werkzeug | Taste | Bedienung |
|---|---|---|
| Bereich | (Leertaste/Enter: Tastatur-Auswahl) | Bereich aufziehen; Klick auf ein Objekt wählt es aus |
| Auswahl / Verschieben | S | Objekt anklicken, ziehen, an den Anfassern Größe ändern |
| Rechteck, Ellipse | R, E | aufziehen (Shift: Quadrat/Kreis); Rahmenfarbe, Strichstärke, Füllfarbe |
| Linie, Pfeil | L, A | vom Anfang zum Ende ziehen (Shift: 45°-Raster); Farbe, Strichstärke, Anfang, Ende |
| Freihand | F | zeichnen; Farbe, Strichstärke, Glätten, Anfang, Ende |
| Text | T | klicken (oder Feld aufziehen = Umbruchbreite) und tippen; Schriftgröße, Farbe, Hintergrund |
| Textmarker | H | halbtransparentes Rechteck aufziehen (Standard Gelb) |
| Verpixeln | P | Rechteck aufziehen; Blockgröße |
| Weichzeichnen | B | Rechteck aufziehen; Stärke |
| Nummern-Stempel | N | klicken: 1, 2, 3 … (nächste Nummer = höchste + 1); Farbe, Größe |
| Virtuelle Maus | M | klicken setzt einen Mauszeiger; Zeigerart wählbar |

- **Linienenden (Anfang, Ende):** Linie, Pfeil und Freihand können an jedem Ende ein Symbol tragen: ohne, Pfeil,
  Punkt, Quadrat oder Dreieck – gefüllt in der Strichfarbe, Größe passend zur Strichstärke, ausgerichtet an der
  Richtung des Linienendes (bei Freihand an der Kurve; eine Pfeilspitze schließt dort lückenlos an den Strich an).
  Ein Pfeil ist eine Linie mit Pfeilspitze am Ende;
  ohne Spitze oder mit Spitzen an beiden Enden ist er genauso einstellbar. Gemerkt wird getrennt je Werkzeug.
- **Füllfarbe (Rechteck, Ellipse):** eigene Farbe unabhängig vom Rahmen – keine, eine Farbe der Palette oder dieselbe
  halbtransparent (50 %). Der Rahmen liegt über der Füllung; gefüllte Objekte lassen sich auch innen anklicken.
- **Text:** Enter übernimmt, Shift+Enter beginnt eine neue Zeile, Esc verwirft, Klick daneben übernimmt.
  Doppelklick auf einen vorhandenen Text (oder Klick mit dem Text-Werkzeug) bearbeitet ihn; leerer Text löscht ihn.
- **Ausgewähltes Objekt:** Entf löscht, Strg+D dupliziert, Esc hebt die Auswahl auf. Farbe, Strichstärke usw. aus der
  Werkzeugleiste wirken auf das ausgewählte Objekt (rückgängig machbar).
- **Verpixeln/Weichzeichnen** werden bei Anzeige und Export aus dem Original berechnet; im Export sind die
  ursprünglichen Pixel nicht enthalten.
- **Virtuelle Maus:** Der aufgenommene Mauszeiger erscheint als Objekt an seiner Originalposition. Er lässt sich
  verschieben, mit dem Augen-Knopf aus- und einblenden (ausgeblendete Zeiger sind im Editor gestrichelt angedeutet),
  in der Art ändern (aufgenommener Zeiger, Pfeil, Hand, Textcursor) und löschen; mit **M** beliebig oft neu einfügen.
- Die Werkzeug-Tastenkürzel gelten nur, solange kein Text bearbeitet wird. Zuletzt verwendete Farbe, Strichstärke,
  Schrift-, Block- und Stempelgröße, Stärke, Füllfarbe, Linienenden und Zeigerart werden gemerkt.
- Rückgängig/Wiederholen umfasst alle Änderungen (Bereich, Zeichnungen, Maus), bis zu 200 Schritte.

**Knöpfe** (Werkzeugleiste am Bereich)

| Knopf | Taste | Ergebnis |
|---|---|---|
| Bereich → Zwischenablage | Enter, Strg+C | Bereich mit Zeichnungen und Maus |
| Bereich speichern | Strg+S | PNG im Exportordner (Standard: `<Ablageordner>\Export`) |
| Bereich speichern unter … | Strg+Shift+S | freier Pfad, PNG oder JPG |
| Ganzer Bildschirm → Zwischenablage | Strg+Shift+C | alle Monitore |
| Aktueller Monitor → Zwischenablage | – | Monitor, auf dem die Maus steht |
| Kopie anlegen | – | speichert den aktuellen Stand als neue Aufnahme (`…_2`) und bearbeitet die Kopie weiter; das Original behält den Stand von jetzt |
| Permanent (Stern) | – | Umschalter, aktiv = blau; wird beim Schließen mitgespeichert |
| Rückgängig / Wiederholen | Strg+Z / Strg+Y | Bereich, Zeichnungen und Maus |
| Abbrechen | Esc | kein Export (bricht zuerst ein laufendes Aufziehen ab) |

Ohne Bereich sind die Bereich-Knöpfe deaktiviert. Die Zwischenablage erhält das Bild als Bitmap und als PNG.
Bei **jedem** Schließen – auch mit Esc – wird der Bearbeitungsstand in der JSON-Datei gespeichert und das
Vorschaubild aktualisiert.

**Fenster-Modus**

Passt die aktuelle Monitor-Anordnung nicht mehr zur Aufnahme, öffnet der Editor als maximiertes Fenster mit denselben
Funktionen: **Strg+Mausrad** zoomt um den Mauszeiger (10–800 %), **Leertaste+Ziehen** oder die **mittlere Maustaste**
verschiebt die Ansicht, das Mausrad (mit Shift waagerecht) ebenfalls. Leertaste ohne Ziehen setzt wie im Vollbild den
Start- bzw. Endpunkt.

## Ablage

```
<Ablageordner>\<JJJJ>\<MM>\
   2026-11-09_14-32-05.png        Original, verlustfrei, wird nie überschrieben
   2026-11-09_14-32-05.json       Metadaten und Bearbeitungsstand
   2026-11-09_14-32-05.thumb.jpg  Vorschaubild (max. 320 px breit)
```

Die JSON enthält seit 0.7 zusätzlich `foreground` (`title`, `process` des Programms im Vordergrund, fehlt bei älteren
Aufnahmen), beim passenden Fenster in `windows` `"foreground": true` und bei permanenten Aufnahmen `"permanent": true`.

- Standard-Ablageordner: `Bilder\ScreenKeeper`
- Einstellungen: `%APPDATA%\ScreenKeeper\settings.json`
- Update-Prüfung: `%APPDATA%\ScreenKeeper\update.json`, heruntergeladene Updates in `%LOCALAPPDATA%\ScreenKeeper\update`
- Protokoll: `%APPDATA%\ScreenKeeper\log.txt`
