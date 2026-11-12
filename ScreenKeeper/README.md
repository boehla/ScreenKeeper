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

Zusätzlich einstellbar (standardmäßig aus):

| Hotkey | Aktion |
|---|---|
| Verzögerte Aufnahme | Countdown (Standard 3 s, auf Wunsch jedes Mal nachfragen), danach Aufnahme + Editor oder nur speichern |
| Druck an Remotesitzung durchreichen | nur im Remotedesktop: schickt ein einfaches Druck an die Sitzung statt aufzunehmen |

**Remotedesktop:** Ein aktives Remotedesktop-Fenster (mstsc, auch im Vollbild) fängt normalerweise alle Tasten ab.
Mit *Hotkeys auch in Remotedesktop-Sitzungen abfangen* (Standard: an) setzt ScreenKeeper seinen Tastatur-Hook neu,
sobald ein solches Fenster aktiv wird, und fängt genau die eingestellten Kombinationen ab; alle anderen Tasten (Win,
Alt+Tab …) gehen unverändert an die Sitzung. Erkannt wird das Fenster an der Fensterklasse (`TscShellContainerClass`,
weitere in `settings.json` unter `remoteSessionWindowClasses`, durch `;` getrennt). Läuft Remotedesktop als
Administrator, ScreenKeeper aber nicht, kommen die Tasten dort nicht an (Hinweis im Protokoll).

**Verzögerte Aufnahme:** für aufgeklappte Menüs, Tooltips und Hover-Zustände. Hotkey drücken, Menü öffnen – nach Ablauf
wird aufgenommen. Rechts unten auf dem Monitor mit dem Mauszeiger zeigt ein kleines Overlay die Restzeit (es nimmt
nie den Fokus und erscheint nicht in der Aufnahme), der Tray-Tooltip ebenfalls. Esc oder ein Klick aufs Tray-Symbol
bricht ab, der Hotkey erneut löst sofort aus, eine normale Aufnahme bricht den Countdown ab. Mit *Jedes Mal nach der
Verzögerung fragen* erscheint vorher ein kleiner Dialog (Sekunden, Schnellwahl 2 / 5 / 10 s, Enter oder Hotkey startet,
Esc bricht ab); der gewählte Wert wird der neue Vorschlag, danach ist das vorherige Fenster wieder vorne. Auch im
Tray-Menü unter *Verzögerte Aufnahme*.

### History

Die History ist das Hauptfenster. Sie zeigt alle Aufnahmen, neueste zuerst, nach Tagen gruppiert (Heute, Gestern,
Datum) – mit Vorschaubild, Uhrzeit, darunter dem **Programm im Vordergrund** bei der Aufnahme (Prozessname und gekürzter
Fenstertitel, der Tooltip zeigt ihn ganz; ist ein Bereich gewählt, steht hier das Fenster, das mehr als die Hälfte des
Bereichs sichtbar füllt – Desktop und Taskleiste ausgenommen), dem Kennzeichen **bearbeitet**, wenn ein Bereich oder Zeichnungen gespeichert
sind, und **permanent** (Stern rechts in der Zeile, gefüllt = permanent). Weitere Einträge werden beim Scrollen nachgeladen, auch Tausende Aufnahmen bleiben flüssig. Schließen versteckt
das Fenster nur; ScreenKeeper läuft im Infobereich weiter.

| Aktion | Bedienung |
|---|---|
| Bearbeiten | Doppelklick, Enter, Knopf oder Kontextmenü |
| In Zwischenablage | Strg+C – der gespeicherte Bereich (ohne Bereich das ganze Bild) mit Zeichnungen und Maus |
| Kopie anlegen | Strg+D, Knopf oder Kontextmenü – dieselbe Aufnahme noch einmal mit eigenem Bereich und eigenen Zeichnungen |
| Permanent | P, Stern in der Zeile, Knopf oder Kontextmenü – umschalten; permanente Aufnahmen löscht das Aufräumen nie |
| Kommentar bearbeiten | K, Knopf oder Kontextmenü – derselbe Dialog wie im Editor; wird sofort in der JSON gespeichert |
| Im Explorer anzeigen | Knopf oder Kontextmenü |
| Löschen | Entf (mit Rückfrage, Mehrfachauswahl möglich) – Bild, JSON und Vorschaubild kommen in den Papierkorb |
| Aktualisieren | F5 |
| Ordner öffnen | öffnet den Ablageordner |

- **Filter:** Von/Bis-Datum, Schnellwahl *Heute*, *7 Tage*, *30 Tage*, *Alle*, zusätzlich der Schalter *Permanent*
  (nur permanente Aufnahmen) und die Suche – sie findet Text im Dateinamen, im Programmnamen und im Fenstertitel des
  Vordergrundprogramms sowie im **Kommentar**. Dafür werden die JSON-Dateien im Hintergrund einmal gelesen und danach
  im Speicher gehalten.
- **Kommentar:** Hat eine Aufnahme einen Kommentar, steht er (einzeilig gekürzt, kursiv, mit Sprechblase) unter dem
  Programm im Vordergrund; der Tooltip zeigt ihn vollständig.
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
  gerade abgefangen wird (über RegisterHotKey, über den Tastatur-Hook oder nicht verfügbar). Dazu die Schalter für
  Remotedesktop-Sitzungen und die Gruppe *Verzögerte Aufnahme* (Sekunden 1–60, jedes Mal fragen, danach Editor oder
  nur speichern).
- **Editor:** Lupe (Vergrößerung 4–16, Größe), Standardfarbe, Textmarkerfarbe, Füllfarbe, Strichstärke und die übrigen
  Standardwerte der Werkzeuge, aufgenommenen Mauszeiger standardmäßig anzeigen.

**Autostart:** Die Einstellung *Mit Windows starten* trägt ScreenKeeper unter
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` ein bzw. entfernt den Eintrag wieder.

**Autostart mit Adminrechten:** Ist ein Programm mit Adminrechten im Vordergrund (z. B. eine Admin-PowerShell), erreicht
die Druck-Taste ein normal laufendes ScreenKeeper nicht (Windows-Schutz UIPI). Mit dieser Einstellung legt ScreenKeeper
in der Aufgabenplanung die Aufgabe `ScreenKeeper\Autostart-<SID>` an, die es bei der Anmeldung mit Adminrechten startet
(EXE am aktuellen Ort). Ein- und Ausschalten fragt einmal per UAC nach; solange die Aufgabe aktiv ist, entfällt der
`Run`-Eintrag. Ein Start aus dem Startmenü startet dann ebenfalls die Aufgabe. Bei der Deinstallation versucht der
Installer, die Aufgabe ohne Adminrechte zu entfernen; gelingt das nicht, bitte in der Aufgabenplanung löschen.

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
  Gleichzeitig laufen durch den Zielpixel ein dezenter waagerechter und ein senkrechter Strich über alle Monitore,
  damit sich der Bereich an weit entfernten Kanten ausrichten lässt.
  **Mausrad** ändert den Zoom der Lupe (4–16×, im Fenster-Modus **Alt+Mausrad**), **+/-** ihre Größe (80–400 px;
  ist ein Nummern-Stempel ausgewählt, ändern **+/-** stattdessen dessen Nummer);
  die Werte gelten auch für die nächsten Aufnahmen. Eine kleine Zeile unter der Lupe nennt Belegung und Werte
  (abschaltbar in den Einstellungen).
- **Tastatur:** Pfeiltasten bewegen den Mauszeiger (bzw. beim Ziehen die Kante) um genau 1 Pixel, mit Shift um 10.
  **Leertaste** oder **Enter** setzt den Startpunkt, ein zweiter Druck den Endpunkt – beide Pixel gehören zum Bereich.
- **Letzten Bereich** (Knopf direkt nach den Werkzeugen, **Strg+L**): setzt den zuletzt gewählten Bereich an
  derselben Bildschirmstelle (ein Rückgängig-Schritt). Gemerkt wird beim Schließen des Vollbild-Editors, wenn der
  Bereich geändert wurde (nicht beim Verwerfen); bei anderer Monitor-Anordnung ist der Knopf ausgegraut.
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
| Text | T | klicken = freier Text, Feld aufziehen = festes Textfeld (Shift: Quadrat), dann tippen; Schriftgröße (Auto, Liste oder eigene Eingabe), Ausrichtung, Farbe, Hintergrund |
| Textmarker | H | Rechteck aufziehen (Standard Gelb); färbt wie ein echter Marker: Text darunter bleibt dunkel |
| Verpixeln | P | Rechteck aufziehen; Blockgröße |
| Weichzeichnen | B | Rechteck aufziehen; Stärke |
| Nummern-Stempel | N | klicken: 1, 2, 3 … (nächste Nummer = höchste + 1); Farbe, Größe; ausgewählter Stempel: Nummer mit „−“/„+“ bzw. Tasten **-**/**+** ändern (1–999) |
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
  Ein **festes Textfeld** wird wie ein Rechteck aufgezogen, verschoben und an allen Anfassern in Breite und Höhe
  geändert (die Schriftgröße bleibt); beim Bearbeiten zeigt ein schwarz-weiß gestrichelter Rahmen die Feldgrenzen. Der
  Text wird im Feld umbrochen; was nicht hineinpasst, wird am Feldrand abgeschnitten (auch im Export).
  **Schriftgröße „Auto“** wählt die größte Schrift, bei der der ganze Text ins Feld passt (je mehr Text, desto kleiner;
  6–400 px); der Knopf zeigt dann z. B. „Auto (31 px)“. Feste Größen kommen aus der Liste oder aus dem Eingabefeld oben
  im Menü (6–400 px, auch Kommazahlen, Enter übernimmt). **Ausrichtung:** 3×3-Raster – horizontal die
  Zeilenausrichtung, vertikal die Lage des Texts im Feld. Ein einfacher Klick ergibt wie bisher freien Text, der mit
  dem Inhalt wächst (feste Schriftgröße, nur horizontale Ausrichtung).
- **Ausgewähltes Objekt:** Entf löscht, Strg+D dupliziert, Esc hebt die Auswahl auf. Farbe, Strichstärke usw. aus der
  Werkzeugleiste wirken auf das ausgewählte Objekt (rückgängig machbar).
- **Verpixeln/Weichzeichnen** werden bei Anzeige und Export aus dem Original berechnet; im Export sind die
  ursprünglichen Pixel nicht enthalten.
- **Virtuelle Maus:** Der aufgenommene Mauszeiger erscheint als Objekt an seiner Originalposition. Er lässt sich
  verschieben, mit dem Augen-Knopf aus- und einblenden (ausgeblendete Zeiger sind im Editor gestrichelt angedeutet),
  in der Art ändern (aufgenommener Zeiger, Pfeil, Hand, Textcursor) und löschen; mit **M** beliebig oft neu einfügen.
- Die Werkzeug-Tastenkürzel gelten nur, solange kein Text bearbeitet wird. Zuletzt verwendete Farbe, Strichstärke,
  Schrift-, Block- und Stempelgröße, Stärke, Füllfarbe, Linienenden und Zeigerart werden gemerkt, beim Text auch
  Auto/feste Schriftgröße, Ausrichtung und Hintergrund – auch wenn sie an einem vorhandenen Text geändert wurden.
- Rückgängig/Wiederholen umfasst alle Änderungen (Bereich, Zeichnungen, Maus, Kommentar), bis zu 200 Schritte.

**Knöpfe** (Werkzeugleiste am Bereich)

| Knopf | Taste | Ergebnis |
|---|---|---|
| Letzten Bereich (Ecken mit Pfeil, nach den Werkzeugen) | Strg+L | zuletzt gewählten Bereich übernehmen; ausgegraut mit Grund im Tooltip |
| Bereich → Zwischenablage | Enter, Strg+C | Bereich mit Zeichnungen und Maus |
| Bereich speichern | Strg+S | PNG im Exportordner (Standard: `<Ablageordner>\Export`) |
| Bereich speichern unter … | Strg+Shift+S | freier Pfad, PNG oder JPG |
| Ganzer Bildschirm → Zwischenablage | Strg+Shift+C | alle Monitore |
| Aktueller Monitor → Zwischenablage | – | Monitor, auf dem die Maus steht |
| Kopie anlegen | – | speichert den aktuellen Stand als neue Aufnahme (`…_2`) und bearbeitet die Kopie weiter; das Original behält den Stand von jetzt |
| Permanent (Stern) | – | Umschalter, aktiv = blau; wird beim Schließen mitgespeichert |
| Kommentar (Sprechblase) | – | öffnet einen kleinen Dialog für freien, mehrzeiligen Text (Strg+Enter übernimmt, Esc bricht ab, leer = entfernen); blau hinterlegt, wenn ein Kommentar vorhanden ist, der Tooltip zeigt ihn. Ein Rückgängig-Schritt; gespeichert wird mit dem Dokument |
| Rückgängig / Wiederholen | Strg+Z / Strg+Y | Bereich, Zeichnungen und Maus |
| Aufnahme verwerfen (roter Mülleimer mit X) | – | schließt den Editor und speichert nichts: PNG, JSON und Vorschaubild kommen ohne Rückfrage in den Papierkorb, die Aufnahme verschwindet aus der History |
| Abbrechen | Esc | kein Export (bricht zuerst ein laufendes Aufziehen ab) |

Ohne Bereich sind die Bereich-Knöpfe deaktiviert. Die Zwischenablage erhält das Bild als Bitmap und als PNG.
Knöpfe, die den Editor schließen (Ausgabe und Abbrechen), haben ein hellblaues Symbol, „Aufnahme verwerfen“ ein
hellrotes; weiße Symbole lassen den Editor offen.
Bei **jedem** Schließen – auch mit Esc – wird der Bearbeitungsstand in der JSON-Datei gespeichert und das
Vorschaubild aktualisiert (außer beim Verwerfen). Eine verworfene Aufnahme lässt sich aus dem Papierkorb
wiederherstellen.

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
Seit 0.9 steht ein Kommentar in `"comment"` (nur wenn vorhanden).

Im Ablageordner legt ScreenKeeper eine `AGENTS.md` an, die KI-Agenten und Skripten Ordnerstruktur, Dateinamen und das
JSON-Format mit allen Zeichenobjekten erklärt. Sie wird beim Start, beim Wechsel des Ablageordners und bei der ersten
Aufnahme geprüft und nur geschrieben, wenn sie fehlt oder veraltet ist (eigene Änderungen daran gehen also verloren).

- Standard-Ablageordner: `Bilder\ScreenKeeper`
- Einstellungen: `%APPDATA%\ScreenKeeper\settings.json`
- Update-Prüfung: `%APPDATA%\ScreenKeeper\update.json`, heruntergeladene Updates in `%LOCALAPPDATA%\ScreenKeeper\update`
- Protokoll: `%APPDATA%\ScreenKeeper\log.txt`
