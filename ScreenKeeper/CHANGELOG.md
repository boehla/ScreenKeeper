# Changelog

## 0.11.0

- **Textmarker wie ein echter Marker:** Der Textmarker wird nicht mehr halbtransparent darübergelegt, sondern im Modus
  „Multiplizieren“ gezeichnet. Text darunter bleibt kräftig dunkel, weißer Hintergrund wird voll gelb. Der Alpha-Wert
  der Farbe wirkt als Stärke (Standard `#FFFFEB3B` = volle Stärke).
- Unter dem Marker ist zu sehen, was vorher gezeichnet wurde: Rahmen, Pfeile, Stempel und frühere Marker bleiben
  getönt sichtbar, und über einer Verpixelung oder Unschärfe scheint nie das Original durch. Mehrere Marker
  übereinander werden entsprechend dunkler.

## 0.10.1

- **Schriftgröße „Auto“ als Standard:** Ohne gespeicherte Einstellung (Neuinstallation oder Update von 0.9) ist für
  neue Textfelder die automatische Schriftgröße vorgewählt. Wer mit 0.10.0 schon den Editor geschlossen hat, hat „fest“
  gespeichert und wählt „Auto“ einmal im Schriftgröße-Menü.

## 0.10.0

- **Bezeichnung folgt dem Bereich:** Ist ein Bereich gewählt, kommen Titel und Prozess in der History von dem Fenster,
  das mehr als die Hälfte des Bereichs sichtbar füllt. Desktop, Taskleiste und Fenster ohne Titel gewinnen nie; dann
  und ohne Bereich bleibt es beim Vordergrundfenster. Pro Fenster wird dafür die Fensterklasse erfasst (neues
  JSON-Feld `className`).
- **Texteingabe:** Enter fügt eine neue Zeile ein; Strg+Enter oder ein Klick daneben übernimmt den Text (bisher:
  Enter übernimmt, Shift+Enter neue Zeile). Beim Aufziehen des Textfelds zeigt der Mauszeiger das Kreuz wie beim
  Rechteck.
- **Feldgröße während der Eingabe:** Ein festes Textfeld zeigt auch beim Bearbeiten seine acht Anfasser; Ziehen ändert
  Breite/Höhe, die Eingabe läuft weiter. Freier Text (wächst mit dem Inhalt) hat dabei keine Anfasser.
- **Textfeld wie ein Rechteck:** Mit dem Text-Werkzeug aufgezogen (gleiche Vorschau, Begrenzung aufs Bild, Shift =
  Quadrat) entsteht ein festes Textfeld mit Breite **und** Höhe. Es lässt sich verschieben und an allen acht
  Anfassern in Breite und Höhe ändern; die Schriftgröße wird dabei nicht mitskaliert. Der Text wird im Feld
  umbrochen; was nicht hineinpasst, wird am Feldrand abgeschnitten – in Anzeige und Export. Beim Bearbeiten zeigt ein
  schwarz-weiß gestrichelter Rahmen die Feldgrenzen, beim Auswählen der Auswahlrahmen. Rahmen unter 10 × 10 px und ein
  einfacher Klick ergeben wie bisher freien Text, der mit dem Inhalt wächst.
- **Schriftgröße „Auto“:** größte Schrift im 0,5-px-Raster (6–400 px, höchstens eine Zeile so hoch wie das Feld), bei
  der der umbrochene Text ohne Worttrennung samt Innenabstand ins Feld passt – je mehr Text, desto kleiner; passt er
  auch mit 6 px nicht, wird abgeschnitten. Berechnet in `TextLayout` (binäre Suche, Ergebnis zwischengespeichert),
  damit Anzeige, Export, Treffertest und Inline-Bearbeitung dieselbe Größe verwenden; die Inline-Bearbeitung passt die
  Größe beim Tippen an. Der Knopf zeigt „Auto (31 px)“.
- **Eigene Schriftgröße:** Im Menü der Schriftgröße steht über der Liste ein Eingabefeld (6–400 px, Komma oder Punkt,
  optional „px“, auf 0,5 gerundet; Enter übernimmt, Esc schließt). Eine feste Größe schaltet „Auto“ aus. Editor-Kürzel
  greifen während der Eingabe nicht.
- **Ausrichtung:** neuer Knopf mit 3×3-Raster (oben/Mitte/unten × links/Mitte/rechts, Tooltips). Horizontal die
  Zeilenausrichtung, vertikal die Lage des Textblocks im festen Feld (bei freiem Text nur horizontal).
- **Text-Einstellungen gemerkt:** Auto/feste Größe, Schriftgröße und Ausrichtung gelten wie Farbe und Hintergrund für
  das nächste Textfeld – auch wenn sie an einem bearbeiteten oder ausgewählten Text geändert wurden. Neue
  Einstellungen `defaultTextAutoFontSize` und `defaultTextAlignment` (unbekannte Werte → oben links).
- Datenformat (`text`): neue Felder `height` (äußere Feldhöhe; fehlt = freier Text), `autoFontSize` und `alignment`
  (`topLeft` … `bottomRight`, unbekannte Werte bleiben erhalten und wirken wie oben links). Mit `height` ist `width`
  die äußere Feldbreite inklusive Innenabstand – so bleibt das Feld stehen, wenn sich der Innenabstand mit der
  automatischen Schriftgröße ändert; ohne `height` bleibt `width` die Umbruchbreite ohne Innenabstand wie bisher.
  Ältere Dateien sehen unverändert aus, `formatVersion` bleibt 1. AGENTS.md beschreibt die neuen Felder.

## 0.9.1

- **Ausgewähltes Objekt bei aktivem Zeichenwerkzeug:** Ein Klick auf das ausgewählte Objekt oder seine Anfasser
  verschiebt bzw. skaliert es jetzt (gleicher Ablauf und Rückgängig-Schritt wie bei „Auswahl“), statt eine neue
  Zeichnung zu beginnen; der Mauszeiger zeigt Verschieben bzw. Größe ändern an. Klicks daneben zeichnen weiterhin neu.
- **Fix Linienanfang:** Das Menü für das Symbol am Anfang (Linie, Pfeil, Freihand) blieb leer, weil das Spiegeln der
  eingefrorenen Symbol-Geometrie scheiterte. Jetzt wird eine veränderbare Kopie gespiegelt; ein Test prüft die Symbole.

## 0.9.0

- **Kommentar zu Aufnahmen:** neuer Knopf „Kommentar“ (Sprechblase) in der Editor-Werkzeugleiste neben „Permanent“.
  Er öffnet einen kleinen Dialog für freien, mehrzeiligen Text (Strg+Enter übernimmt, Esc bricht ab, leerer Text
  entfernt den Kommentar). Der Dialog ist ein eigenes, vom Editor besessenes Fenster – über dem Vollbild-Editor
  ebenfalls oberstes Fenster und auf dem Monitor der Werkzeugleiste zentriert –, Editor-Kürzel greifen währenddessen
  nicht. Übernehmen ist ein Rückgängig-Schritt; der Knopf ist blau hinterlegt, wenn ein Kommentar vorhanden ist, und
  zeigt ihn im Tooltip. Gespeichert wird mit dem Dokument.
- **History:** Der Kommentar steht einzeilig gekürzt (kursiv, mit Sprechblase) unter dem Programm im Vordergrund, der
  Tooltip zeigt ihn ganz. „Kommentar bearbeiten …“ (Taste K, Knopf, Kontextmenü) öffnet denselben Dialog und speichert
  die JSON sofort atomar (fehlt sie, wird sie angelegt); ein offener Editor derselben Aufnahme übernimmt den Text. Die
  Suche findet auch Text im Kommentar (Platzhalter „Suche: Name, Programm, Titel, Kommentar“); der Zwischenspeicher
  der Kennzeichen wird beim Speichern einer JSON verworfen.
- Datenformat: optionales Feld `comment` (String, Zeilenumbruch `\r\n`) nach `permanent`; nur geschrieben, wenn nicht
  leer. `formatVersion` bleibt 1, ältere Dateien ohne das Feld laden unverändert. Kopien übernehmen den Kommentar.
- **AGENTS.md im Ablageordner:** ScreenKeeper legt neben den Jahresordnern eine `AGENTS.md` an, die KI-Agenten und
  Skripten Ordnerstruktur, Dateinamen (Muster, Kollisionssuffix, Kopien), alle JSON-Felder, alle Zeichenobjekte mit
  ihren Feldern und Standardwerten sowie das Koordinatensystem erklärt. Der Inhalt ist im Programm eingebettet
  (`Resources\AGENTS.template.md`, mit Programmversion im Kopf). Geprüft wird beim Start, nach dem Speichern der
  Einstellungen und bei der ersten Aufnahme je Ablageordner (auch headless mit `--capture --folder`); geschrieben nur,
  wenn die Datei fehlt oder abweicht, und nur in einen vorhandenen Ablageordner. Fehler werden nur protokolliert.
  History, Aufräumen und Export lesen nur `JJJJ\MM\*.png` und ignorieren sie. Ein Unit-Test stellt sicher, dass jeder
  registrierte Annotationstyp und jedes JSON-Feld darin beschrieben ist.
- **Hotkeys im Remotedesktop (auch Vollbild):** mstsc setzt beim Aktivieren seines Fensters einen eigenen
  Low-Level-Tastatur-Hook, der vor ScreenKeeper lag und die Tasten nicht weiterreichte (auch RegisterHotKey kam nicht
  an). ScreenKeeper beobachtet jetzt den Vordergrund (`SetWinEventHook`, im Hook-Thread) und setzt seinen Hook neu,
  sobald ein Fenster der Klasse `TscShellContainerClass` aktiv wird – sofort und nach 250 ms noch einmal; auch beim
  Start bzw. Übernehmen der Einstellungen wird geprüft, ob schon eines vorne ist. Solange laufen alle eingestellten
  Hotkeys über den Hook; nur genau diese Kombinationen (und ihr Loslassen) werden geschluckt, alles andere (Win,
  Alt+Tab …) geht unverändert an die Sitzung. Außerhalb von RDP bleibt alles wie bisher: Der Hook ist dann nur
  installiert, wenn ein Druck-Fallback (oder Esc im Countdown) ihn braucht, und lässt die per RegisterHotKey
  registrierten Kombinationen durch – keine Doppelauslösung. Zusatztasten verfolgt der Hook im RDP selbst mit, weil
  mstsc sie lokal schluckt. Neue Einstellungen `captureHotkeysInRemoteSessions` (Standard an, Schalter „Hotkeys auch
  in Remotedesktop-Sitzungen abfangen“) und `remoteSessionWindowClasses` (nur in `settings.json`, mehrere durch `;`
  oder `,`). Läuft das Remote-Fenster mit Administratorrechten, ScreenKeeper aber nicht, steht einmal ein Hinweis im
  Protokoll (UIPI). Während der Hotkey-Aufzeichnung im Einstellungsfenster ist auch der Hook ausgesetzt.
- **Druck an Remotesitzung durchreichen:** optionaler Hotkey `hotkeyRemotePrintScreen` (Standard aus). Nur bei
  Remote-Fenster vorne und eingeschalteter Remote-Erfassung: Der Hook schluckt die Kombination, danach sendet
  ScreenKeeper per `SendInput` (außerhalb des Hook-Callbacks) die gehaltenen Zusatztasten los, Druck, und die
  Zusatztasten wieder gedrückt; bei Alt/Win ohne Strg vorher ein kurzes Strg, damit die Sitzung kein einzelnes Alt/Win
  sieht. Die eigenen Ereignisse tragen eine Kennung in `dwExtraInfo` und gehen im eigenen Hook unverändert weiter.
  Konflikte mit anderen Hotkeys werden wie gewohnt geprüft (keine Probe-Registrierung, da er nie registriert wird).
- **Verzögerte Aufnahme:** neuer Hotkey `hotkeyCaptureDelayed` (Standard aus) und Tray-Eintrag „Verzögerte Aufnahme“.
  Countdown (`captureDelaySeconds`, Standard 3, 1–60) mit kleinem Overlay rechts unten auf dem Monitor mit dem
  Mauszeiger (oberstes Fenster, nimmt nie den Fokus, klick-durchlässig, nicht in der Taskleiste, per
  `WDA_EXCLUDEFROMCAPTURE` von Aufnahmen ausgenommen und vor der Aufnahme zusätzlich ausgeblendet) und Restzeit im
  Tray-Tooltip. Esc (für die Dauer des Countdowns über den Tastatur-Hook, auch im RDP; Esc wird dabei geschluckt) oder
  ein Klick aufs Tray-Symbol bricht ab, der Hotkey erneut löst sofort aus, eine normale Aufnahme bricht den Countdown
  ab. Mit `captureDelayAskEachTime` erst ein kleiner Dialog (Sekunden, Schnellwahl 2 / 5 / 10 s, Enter oder erneuter
  Hotkey startet, Esc bricht ab); der gewählte Wert wird gespeichert, danach geht der Fokus an das vorherige Fenster
  zurück. Danach Aufnahme + Editor oder nur speichern (`delayedCaptureAction`: `edit`/`save`, mit Benachrichtigung).
  Bei offenem Editor holt der Hotkey nur den Editor nach vorne. Alles im Einstellungsfenster (Hotkeys) einstellbar.
- **Letzten Bereich übernehmen:** neuer Knopf in der Editor-Werkzeugleiste direkt nach den Werkzeugen (Bereichsecken
  mit Rückwärtspfeil), Kürzel **Strg+L** (L allein bleibt das Linien-Werkzeug). Er setzt den zuletzt gewählten Bereich
  an derselben Bildschirmstelle – ein Rückgängig-Schritt über denselben Befehl wie das Aufziehen; ragt er teilweise
  hinaus, wird er auf das Bild begrenzt. Gemerkt wird beim Schließen bzw. Ausgeben des Editors (nicht beim Verwerfen)
  in `lastSelection` (Bildschirmkoordinaten des virtuellen Desktops: Bildpixel + Ursprung des virtuellen Bildschirms),
  und zwar nur, wenn die Monitor-Anordnung der Aufnahme der aktuellen entspricht (Vollbild-Editor) und der Bereich in
  dieser Sitzung geändert wurde – eine alte Aufnahme aus der History, die nur angesehen wird, überschreibt ihn nicht.
  Ausgegraut (Tooltip nennt den Grund), wenn noch keiner gemerkt ist, die Anordnung abweicht (Fenster-Editor) oder der
  Bereich das Bild nicht schneidet. Ungültige Werte in der Datei werden verworfen.
- **Lupe im Editor verstellen:** Solange die Lupe zu sehen ist, ändert das Mausrad den Zoom (ganze Schritte, 4–16;
  im Fenster-Editor **Alt+Rad**, weil das Rad dort scrollt bzw. mit Strg die Ansicht zoomt; das Loslassen von Alt
  aktiviert danach nicht das Fenstermenü) und **+/-** (Haupttastatur und Ziffernblock) die Größe in 10er-Schritten
  im Bereich des Schiebereglers (80–400 px; Werte außerhalb werden in den Bereich geführt). Nicht während einer
  Texteingabe. Die Lupe wird sofort neu aufgebaut und platziert. Die Werte gelten auch für die nächsten Aufnahmen:
  gespeichert wird gesammelt nach 1 s Ruhe bzw. beim Schließen des Editors; ein offenes Einstellungsfenster übernimmt
  die neuen Werte (statt sie beim Übernehmen mit den alten zu überschreiben).
- **Tastenhilfe unter der Lupe:** zweite, kleinere Zeile mit der geltenden Belegung und den aktuellen Werten (z. B.
  „Rad: Zoom 8×   +/-: Größe 160 px“, im Fenster-Editor „Alt+Rad: …“); abschaltbar im Einstellungsfenster
  („Tastenhilfe unter der Lupe anzeigen“, `showMagnifierKeyHints`, Standard an).
- **Tray-Symbol zeigt die Hotkeys:** Der Tooltip listet je eingestelltem Hotkey eine Zeile (Aufnahme, nur speichern,
  verzögerte Aufnahme, History) statt des festen Textes „Druck: Aufnahme“, und das Kontextmenü zeigt die Tasten rechts
  neben den Einträgen. Beides folgt geänderten Einstellungen sofort.
- Testschalter `--open <json> --test-magnifier` (Lupe per Code verstellen, Tastenhilfe und letzten Bereich prüfen,
  speichert keine Einstellungen).
- Intern: `PrintScreenFilter` heißt jetzt `HotkeyFilter` und gilt für beliebige Tasten (je Kombination „immer“ oder
  „nur bei Remote-Fenster“, threadsicheres Remote-Flag); die Ablauflogik der verzögerten Aufnahme steckt UI-frei in
  `DelayedCaptureController`. Das Neu-Registrieren der Hotkeys vergleicht alle hotkey-relevanten Einstellungen.
- **Hilfslinien beim Bereich:** Beim Wählen des Bereichs (vor der ersten Auswahl, beim Aufziehen, beim Ziehen der
  Anfasser und bei der Tastatur-Auswahl – also immer, wenn die Lupe sichtbar ist) geht durch den Zielpixel je ein
  dezenter waagerechter und senkrechter Strich über die ganze Fläche, im Vollbild über alle Monitore. Die Linien sind
  nur Anzeige und landen nie im ausgegebenen Bild.
- **Stern „permanent“ gelb:** Ist eine Aufnahme permanent markiert, ist der Stern in der Editor-Werkzeugleiste gelb
  gefüllt (wie das Abzeichen in der History). Der blaue Hintergrund des Knopfs entfällt – er verschwand ohnehin,
  solange die Maus über dem Knopf war.


## 0.8.0

- **Aufnahme verwerfen:** neuer Knopf in der Editor-Werkzeugleiste vor „Abbrechen“ (Mülleimer mit X, Tooltip
  „Aufnahme verwerfen (nicht speichern)“). Er schließt den Editor, ohne den Bearbeitungsstand oder das Vorschaubild zu
  speichern, und verschiebt PNG, JSON und Vorschaubild ohne Rückfrage in den Papierkorb (wie Löschen in der History).
  Ein noch laufendes Hintergrund-Schreiben einer neuen Aufnahme wird vorher abgewartet; das Löschen zählt zu den
  ausstehenden Schreibvorgängen, die das Programmende abwartet. Gilt auch für eine aus der History (oder mit `--open`)
  geöffnete Aufnahme; die History entfernt den Eintrag. Ein offenes Textfeld wird dabei verworfen statt übernommen.
- **Schließende Knöpfe farblich abgesetzt:** Bereich → Zwischenablage, Bereich speichern, Bereich speichern unter …,
  Ganzer Bildschirm → Zwischenablage, Aktueller Monitor → Zwischenablage und Abbrechen haben ein hellblaues Symbol,
  „Aufnahme verwerfen“ ein hellrotes (eigene Brush-/Style-Ressourcen `ClosingIconBrush`/`ClosingIcon` und
  `DiscardIconBrush`/`DiscardIcon` in `EditorToolbar.xaml`). Knöpfe, die den Editor offen lassen, bleiben weiß.
- **Installer:** `ScreenKeeper-<Version>-win-x64-setup.exe` im Release (Inno Setup, `installer/ScreenKeeper.iss`):
  Installation pro Benutzer ohne Adminrechte, Startmenü-Eintrag, optional Desktop-Symbol und „Mit Windows starten“
  (vorausgewählt, derselbe Registry-Wert wie der Autostart-Schalter in den Einstellungen). Das In-App-Update
  funktioniert auch bei installierten Kopien.

## 0.7.0

- Einstellungen: neues Feld `settingsVersion`. Dateien aus 0.6.x ohne dieses Feld werden einmalig umgestellt: eine
  Aufbewahrung von 0 (damals Standard „aus“) wird zu 30 Tagen; andere bewusst gesetzte Werte bleiben. Ab 0.7.0 bleibt
  eine bewusst gesetzte 0 (aus) erhalten.
- **Linienenden:** Linie, Pfeil und Freihand haben in der Werkzeugleiste die Optionen „Anfang“ und „Ende“ mit den
  Symbolen ohne, Pfeil, Punkt, Quadrat und Dreieck (Menü mit Symbol und Text). Die Symbole sind gefüllt in der
  Strichfarbe, wachsen mit der Strichstärke wie die bisherige Pfeilspitze und folgen der Richtung des Linienendes
  (bei geglätteter Freihand der Tangente der Kurve). Bei einer Pfeilspitze endet der Strich an ihrer Grundlinie, damit
  nichts übersteht (bei Freihand wird die Kurve dafür gekürzt); an einer Freihand-Linie richtet sich die Spitze nach
  der Sehne über ihre Länge, damit sie auch bei einer Biegung kurz vor dem Ende ohne Lücke auf dem Strich sitzt.
  Punkt, Quadrat und Dreieck sitzen mittig auf dem Endpunkt.
- Treffertest und Auswahlrahmen berücksichtigen die Endsymbole (Fläche des Symbols plus Toleranz).
- Datenformat: `startCap`/`endCap` (`none`, `arrow`, `dot`, `square`, `triangle`) bei `line`, `arrow`, `freehand`.
  Fehlt ein Feld (Dateien bis 0.6) oder ist es `null`, gilt der bisherige Standard des Typs: Linie und Freihand ohne,
  Pfeil mit Spitze am Ende – ältere Aufnahmen sehen unverändert aus. Unbekannte Werte bleiben erhalten und werden
  ohne Symbol gezeichnet.
- **Eigene Füllfarbe für Rechteck und Ellipse:** statt „Füllung an/aus“ (in Rahmenfarbe) ein eigener Farbwähler
  „Füllung“ mit „keine“, der Palette und der Palette mit 50 % Deckkraft (Schachbrett zeigt die Transparenz). Die
  Rahmenfarbe ändert die Füllung nicht mehr mit; der Rahmen liegt über der Füllung.
- Datenformat: `fill` ist weiter eine Farbe `#AARRGGBB` (Transparenz erlaubt). Tolerant gelesen werden zusätzlich
  `true` (→ Rahmenfarbe), `false` und `null` (→ keine Füllung); geschrieben wird immer die Farbe.
- Der Text-Hintergrund ist ein eigener Knopf „Hintergrund“ (vorher teilte er sich den Füllungs-Knopf).
- Einstellungen: `defaultFillColor` (leer = keine Füllung) ersetzt `defaultFill`; ein altes `"defaultFill": true` wird
  beim Laden zur Standardfarbe als Füllfarbe und danach nicht mehr geschrieben. Neu `defaultLineStartCap`,
  `defaultLineEndCap`, `defaultArrowStartCap`, `defaultArrowEndCap`, `defaultFreehandStartCap`,
  `defaultFreehandEndCap` (je Typ getrennt, ungültige Werte → Standard). Im Einstellungsfenster wählt eine Farbreihe
  „Füllung (Rechteck, Ellipse)“ die Standard-Füllfarbe.
- Testhilfe `--test-annotate` füllt das Rechteck halbtransparent und setzt Endsymbole an Linie, Pfeil und Freihand.
- Neue Tests: Serialisierung (neue Felder, alte Dateien ohne Felder, `fill` als bool), Geometrie und Treffertest der
  Endsymbole, Pixeltests (Endsymbole in Strichfarbe, Füllfarbe innen, Rahmenfarbe außen), Editor (Option wirkt auf
  das ausgewählte Objekt als ein Rückgängig-Schritt und wird je Typ als Standard gemerkt).
- **Kopie anlegen:** dieselbe Aufnahme mehrfach mit unterschiedlichem Bereich und unterschiedlichen Zeichnungen. In der
  History Knopf, Kontextmenü und Strg+D: PNG, JSON und Vorschaubild werden unter dem nächsten freien Namen (`…_2`,
  `…_3`; die Kopie einer Kopie hängt nicht `_2_2` an) im selben Ordner angelegt, alle Zeichenobjekte bekommen neue Ids
  (auch unbekannte Typen), die Kopie ist nicht permanent; sie erscheint direkt über dem Original und ist ausgewählt.
  Laufende Hintergrund-Schreibvorgänge des Originals werden abgewartet; schlägt etwas fehl, bleibt keine halbe Kopie
  liegen. Im Editor speichert der Knopf „Kopie anlegen“ den aktuellen Stand als Kopie, schließt das Original mit
  genau diesem Stand und bearbeitet die Kopie im selben Modus weiter (Bild aus dem Speicher, kein erneutes Laden).
- **Permanent:** Aufnahmen lassen sich als permanent markieren – in der History per Stern in der Zeile, Knopf,
  Kontextmenü oder Taste P (bei Mehrfachauswahl: alle permanent bzw., wenn schon alle permanent sind, alle nicht
  mehr), im Editor mit dem Umschaltknopf „Permanent“ (wird beim Schließen mitgespeichert, kein Rückgängig-Schritt).
  Kennzeichen „permanent“ in der Zeile und Filter-Schalter „Permanent“. Fehlt die JSON, wird sie angelegt; ist die
  Aufnahme gerade im Editor offen, übernimmt der Editor den Wert.
- **Aufräumen:** Standard für `retentionDays` ist jetzt 30 Tage (bisher 0 = aus); eine settings.json ohne das Feld
  bekommt den neuen Standard, ein ausdrücklich gespeichertes 0 bleibt aus. Permanente Aufnahmen und solche mit
  unlesbarer JSON werden übersprungen (ohne JSON = nicht permanent); das Protokoll nennt verschobene und behaltene.
  Text im Einstellungsfenster: „Aufnahmen nach X Tagen in den Papierkorb verschieben, außer permanente (0 = aus)“.
- **Programm im Vordergrund:** Bei der Aufnahme wird das aktive Fenster (`GetForegroundWindow`) mit Titel und
  Prozessname festgehalten. Die History zeigt es unter der Uhrzeit (Titel gekürzt, Tooltip vollständig); die Suche
  findet zusätzlich Prozessname und Fenstertitel. Die JSON wird dafür je Eintrag nur einmal im Hintergrund gelesen
  und mit ihrer Änderungszeit zwischengespeichert (Kennzeichen „bearbeitet“, „permanent“ und Programm teilen sich
  diesen Lesevorgang).
- **Updates ohne Dialoge:** `UpdateService` fragt 30 s nach dem Start und danach höchstens einmal je 24 h
  `api.github.com/repos/boehla/ScreenKeeper/releases/latest` ab (Timeout 10 s, Fehler nur im Protokoll; Zeitpunkt und
  gefundene Version in `%APPDATA%\ScreenKeeper\update.json`). Ist eine neuere Version da: Link „Version x verfügbar –
  installieren“ in der History-Statusleiste, Eintrag „Update auf x installieren“ im Tray-Menü, „· Update verfügbar“ im
  Tooltip, Ergebniszeile mit „Jetzt prüfen“ in den Einstellungen. Installieren lädt das passende ZIP (Single-File mit
  Laufzeit → `…-win-x64.zip`, sonst `…-win-x64-framework-dependent.zip`) nach `%LOCALAPPDATA%\ScreenKeeper\update\<v>\`,
  entpackt und prüft es, speichert einen offenen Editor, wartet auf Schreibvorgänge, startet ein verstecktes
  PowerShell-Skript (wartet auf das Prozessende, ersetzt die EXE mit Wiederholung bis 30 s, startet neu, löscht sich)
  und beendet ScreenKeeper. Entwicklungs-Builds öffnen nur die Release-Seite. Einstellung `checkForUpdates`
  (Standard an).
- Datenformat: neu `foreground` (`title`, `process`; null/fehlend = unbekannt), `windows[].foreground` (nur `true`
  wird geschrieben) und `permanent` (nur `true` wird geschrieben, fehlend = false).
- Testhilfen: `--history --folder <Ordner> --test-duplicate <Name>`, `--test-permanent <Name>`, `--retention --folder
  <Ordner> [--days n] [--dry-run]`; `--test-history` misst zusätzlich die Suche über Programm/Titel.
- Neue Tests (20): Kopie (Dateien, neue Ids, Original unverändert, Namen, ohne JSON, History-Einfügen), Permanent
  (Rundreise, JSON anlegen, Filter), Aufräumen (überspringt permanente und unlesbare, Standard 30), Vordergrund
  (Serialisierung, Anzeige, Suche, einmaliges Lesen), Update (Versionsvergleich, Asset-Wahl aus einer gespeicherten
  GitHub-Antwort, „kein Update“, gemerkter Zustand, Entpacken, Skript).

## 0.6.1

- Das zuletzt gezeichnete Objekt (auch Stempel, Zeiger und Text) bleibt ausgewählt; Farbe und Optionen in der Werkzeugleiste wirken damit sofort auf dieses Objekt.
- Der Beginn einer neuen Zeichnung hebt die Auswahl auf; bei aktivem Zeichenwerkzeug schließt Esc weiterhin direkt.

## 0.6.0 – Phase 6: Feinschliff (09.11.2026)

### Fehlerbehebungen

- **Druck-Taste über den Tastatur-Hook ging jedes zweite Mal verloren:** Der Low-Level-Hook lief im UI-Thread. Während
  Aufnahme und Editor-Aufbau (dort 0,2–0,6 s) konnte Windows ihn nicht aufrufen, das Loslassen der Druck-Taste kam
  nicht an, der Hook hielt die Taste für „noch gedrückt“ und verschluckte den nächsten Druck (nachgestellt mit
  `SendInput` bei laufendem Greenshot: 2. von 3 Drücken ohne Wirkung). Außerdem kann Windows einen Hook, der zu lange
  braucht, stillschweigend entfernen. Der Hook läuft jetzt in einem eigenen Thread mit eigener Nachrichtenschleife
  (`KeyboardHook`), die Entscheidung trifft ein testbarer `PrintScreenFilter`; das Loslassen einer abgefangenen
  Druck-Taste wird immer unterdrückt, auch wenn Strg/Shift vorher losgelassen wurden
- Druck bei offenem Editor holt den Editor jetzt zuverlässig in den Vordergrund (vorher nur `Activate`, das Windows bei
  einem Hook-Hotkey oft verweigert); Strg+Druck („nur speichern“) bei offenem Editor fotografiert nicht mehr den Editor,
  sondern holt ihn ebenfalls nach vorne
- Vorschaubild einer neuen Aufnahme wird im Hintergrund geschrieben (vorher synchron vor dem Öffnen des Editors).
  Damit sich Schreibvorgänge nicht überholen, warten Vorschaubild-Aktualisierung beim Schließen des Editors, das
  Nacherzeugen in der History und das Löschen auf ein noch laufendes Hintergrund-Schreiben derselben Datei
- Vollbild-Editor: nach einem DPI-Wechsel (`WM_DPICHANGED`) wird das Fenster wieder exakt über den virtuellen
  Bildschirm gelegt (WPF verschiebt es sonst auf das vorgeschlagene Rechteck)
- Kann der Bearbeitungsstand beim Schließen nicht gespeichert werden (z. B. Ablageordner schreibgeschützt oder
  Laufwerk getrennt), erscheint eine Meldung statt nur eines Protokolleintrags
- Datenformat: `ocrText` wird wie in der Spezifikation immer geschrieben (auch `null`); unbekannte Felder in
  Zeichenobjekten, Monitoren, Fenstern und Mauszeiger bleiben beim Speichern erhalten (vorher nur auf oberster Ebene und
  bei unbekannten Objekttypen); eine höhere `formatVersion` wird gelesen und protokolliert (Warnung)

### Leistung

- Editor wird beim Programmstart einmal unsichtbar vorgewärmt (außerhalb des Bildschirms, ohne Fokus, ohne Speichern).
  Erste Aufnahme nach dem Start bis zum sichtbaren Editor: vorher 606–771 ms, jetzt 251–324 ms; jede weitere
  142–192 ms (1920 × 1080)
- Protokoll zeigt die Dauer der Aufnahmeschritte (Zeiger, Bild, Monitore/Fenster) und des Speicherns beim Schließen

### Sonstiges

- Umrechnung Bildschirm ↔ Bildpixel zentral in `ScreenCoordinates` (Aufnahme, Mauszeiger, Fenster, Pfeiltasten,
  Vergleich der Monitor-Anordnung)
- Nicht verwendeter Text `AppName` entfernt
- Neue Tests (11): negative Koordinaten mit zwei Monitoren, Lupe/Werkzeugleiste je Monitor, DPI 150 %,
  Monitor-Anordnung, Druck-Filter des Hooks, verlustfreie Rundreise des Spezifikationsbeispiels, unbekannte Felder,
  defekte JSON, Muster ohne Sekunden mit sofortigem Vorschaubild-Update, Ablage auf fehlendem Laufwerk, 2 × 4K
- Testschalter `--capture --test-capture-timing` (Zeit bis zum sichtbaren Editor, 3 Läufe); Prüfprotokoll in
  `ABNAHME.md`, Testanleitung in `TESTS.md` (Repo-Root)

## 0.5.0 – Phase 4 und 5: History, nachträgliches Bearbeiten, Einstellungen (09.11.2026)

### Phase 4 – History und nachträgliches Bearbeiten

- History-Fenster als Hauptfenster (in der Taskleiste; Größe, Position und Maximiert werden gemerkt). Schließen
  versteckt es nur, beendet wird über das Tray-Menü. Tray-Doppelklick, Tray-Menü, Hotkey und ein zweiter Programmstart
  öffnen bzw. aktivieren es
- Aufnahmen neueste zuerst, nach Tagen gruppiert („Heute“, „Gestern“, sonst Datum lang, mit Anzahl). Je Eintrag
  Vorschaubild, Uhrzeit, Kennzeichen „bearbeitet“ (Bereich oder Zeichnungen), „nur Bild“ (ohne JSON), „JSON defekt“;
  Tooltip mit Dateiname, Größe und Ordner
- Schnell auch bei vielen Aufnahmen: Monatsordner werden im Hintergrund von neu nach alt gelesen, Einträge in Blöcken zu 60
  beim Scrollen nachgeladen, die Liste ist virtualisiert. Vorschaubilder werden erst beim Anzeigen im Hintergrund geladen
  (verkleinert dekodiert, höchstens 300 im Speicher); fehlende Vorschaubilder werden aus dem PNG erzeugt.
  5.000 Aufnahmen: erster Block nach ca. 0,6 s, alle nach ca. 3,5 s
- Filter: Von/Bis (Datum), Schnellwahl Heute / 7 Tage / 30 Tage / Alle, Suche im Dateinamen. Monatsordner außerhalb
  des Zeitraums werden gar nicht erst gelesen
- Aktionen über Knöpfe, Kontextmenü und Tasten: Bearbeiten (Doppelklick, Enter), In Zwischenablage (Strg+C, Bereich bzw.
  ganzes Bild mit Zeichnungen und Maus), Im Explorer anzeigen, Löschen (Entf, Rückfrage, Mehrfachauswahl; PNG, JSON und
  Vorschaubild in den Papierkorb), Aktualisieren (F5), Ordner öffnen
- Bearbeiten öffnet denselben Editor wie eine neue Aufnahme (Vollbild bei gleicher Monitor-Anordnung, sonst
  Fenster-Modus). Aufnahmen ohne JSON (auch von Hand abgelegte PNGs) erscheinen mit Zeit aus dem Dateinamen bzw.
  Änderungsdatum und bekommen beim ersten Bearbeiten ein Dokument (ein Monitor in Bildgröße); eine defekte JSON wird
  als `.json.broken` gesichert und ersetzt
- Nach dem Schließen des Editors wird der Eintrag aktualisiert (Vorschaubild, Kennzeichen); neue Aufnahmen erscheinen
  sofort oben in der Liste
- Aufbewahrung: Ist `retentionDays` > 0, werden Aufnahmen, die älter sind, beim Start und einmal täglich in den
  Papierkorb verschoben (alle drei Dateien, Protokolleintrag). Standard: aus
- Neue Einstellung `historyWindow` (Lage des Fensters)

### Phase 5 – Einstellungen

- Einstellungsfenster mit den Seiten Allgemein, Hotkeys und Editor; OK, Abbrechen, Übernehmen; Fehler werden direkt
  unter dem Feld angezeigt. Alle Einstellungen wirken ohne Neustart
- Allgemein: Ablageordner und Exportordner (Ordnerwahl, leer = Standard, Anzeige des verwendeten Pfads,
  Umgebungsvariablen erlaubt), Dateinamen-Muster mit Vorschau und Prüfung (ungültige Zeichen, unbekannte oder offene
  Platzhalter), Aufbewahrung in Tagen (0 = aus), Autostart mit Windows (Zustand aus der Registry), Benachrichtigung bei
  „nur speichern“
- Hotkeys: Tastenaufzeichnung (Feld anklicken, Kombination drücken – auch Druck; Esc bricht ab, Entf löscht), während der
  Aufzeichnung sind die globalen Hotkeys ausgesetzt. Status je Hotkey (aktiv, über Tastatur-Hook, nicht verfügbar) und
  „Status Druck-Taste: über RegisterHotKey / über Tastatur-Hook / nicht verfügbar“
- Konfliktprüfung beim Speichern: gleiche Hotkeys, ungültige Hotkeys und Buchstaben/Ziffern ohne Strg/Alt/Win sind
  Fehler; Probe-Registrierung geänderter Hotkeys (von einem anderen Programm belegt → Hinweis, bei Druck mit Verweis auf den
  Tastatur-Hook) und Hinweis bei aktiver Windows-Einstellung „Druck öffnet Bildschirmausschnitt“ – speichern ist nach
  Rückfrage trotzdem möglich
- Editor: Lupe (Vergrößerung 4–16, Größe), Standardfarbe und Textmarkerfarbe (Palette des Editors), Strichstärke,
  Schriftgröße, Blockgröße, Weichzeichner-Stärke, Stempelgröße, Zeigerart, Füllung, Text-Hintergrund, Glätten,
  aufgenommenen Mauszeiger standardmäßig anzeigen
- Hotkeys werden nach dem Speichern nur bei Änderung neu registriert; Ablageordner, Exportordner und Muster gelten ab der
  nächsten Aufnahme, die History lädt bei geändertem Ablageordner neu, die Aufbewahrung wird neu bewertet
- Tests für Tagesgruppierung (inkl. Tages- und Zeitzonengrenzen), blockweises Laden, Filter, Aufnahmen ohne JSON,
  Zeit aus Dateinamen, Aufbewahrung (Probelauf), Muster-Prüfung, Hotkey-Konflikte und Probe-Registrierung, Einstellungen
  mit allen Feldern, Anlegen eines Dokuments für ein PNG

## 0.3.0 – Phase 3: Zeichenwerkzeuge, virtuelle Maus, Rückgängig (09.11.2026)

- Zeichenwerkzeuge als einzelne, nachträglich änderbare Objekte (alle mit Live-Vorschau beim Ziehen und einem
  Rückgängig-Schritt je Objekt; zu kleine Objekte werden verworfen):
  Rechteck (R) und Ellipse (E) mit Rahmenfarbe, Strichstärke und optionaler Füllung (Shift: Quadrat/Kreis),
  Linie (L) und Pfeil (A) mit gefüllter, zur Strichstärke proportionaler Spitze (Shift: 45°-Raster),
  Freihand (F) mit Glättung (Catmull-Rom → Bézier, Punkte werden ausgedünnt),
  Text (T) mit Inline-Bearbeitung direkt im Bild (Enter übernimmt, Shift+Enter neue Zeile, Esc verwirft, Aufziehen legt
  die Umbruchbreite fest, optionaler Hintergrund in Kontrastfarbe), Textmarker (H, halbtransparent, Standard Gelb),
  Verpixeln (P, Blockgröße) und Weichzeichnen (B, Stärke), Nummern-Stempel (N, fortlaufend, ohne Umnummerieren)
- Verpixeln und Weichzeichnen werden bei Anzeige und Export aus dem Original berechnet (Blockfarbe = Mittelwert der
  Originalpixel bzw. dreifacher Box-Blur ≈ Gauß); im Export sind keine Originalpixel des Bereichs enthalten.
  Die Ergebnisse werden zwischengespeichert, damit das Ziehen flüssig bleibt
- Virtuelle Maus (M): Klick setzt einen Zeiger der gewählten Art (aufgenommener Zeiger – nur wenn erfasst –, Pfeil,
  Hand, Textcursor); ausgewählte Zeiger lassen sich aus- und einblenden, die Art ändern, verschieben und löschen.
  Ausgeblendete Zeiger sind im Editor gestrichelt angedeutet (nicht im Export)
- Auswahl (S): Objekt anklicken (oberstes zuerst; Rahmen nur am Strich, Linien über den Abstand), verschieben,
  Größe über Anfasser ändern (8 Anfasser, Linie/Pfeil: Endpunkte, Stempel: Durchmesser, Text: Seiten = Umbruchbreite,
  Ecken = Schriftgröße), Entf löscht, Strg+D dupliziert, Esc hebt die Auswahl auf, Doppelklick auf Text bearbeitet ihn.
  Auch im Werkzeug „Bereich“ wählt ein Klick auf ein Objekt dieses aus, ein Klick ins Leere arbeitet wie bisher
- Werkzeugleiste: Werkzeugknöpfe mit aktivem Zustand und Tastenkürzeln, dazu nur die passenden Optionen –
  Farbpalette, Strichstärke (1–12 px), Füllung/Hintergrund, Glätten, Schriftgröße, Blockgröße, Stärke, Stempelgröße,
  Zeigerart, Zeiger ein-/ausblenden, Objekt löschen. Änderungen wirken auf das ausgewählte Objekt (als
  Rückgängig-Schritt) bzw. den gerade bearbeiteten Text
- Zuletzt verwendete Werte werden beim Schließen des Editors gemerkt (neue Einstellungen `defaultFontSize`,
  `defaultBlockSize`, `defaultBlurRadius`, `defaultFill`, `defaultTextBackground`, `defaultSmooth`,
  `defaultStampSize`, `defaultCursorKind`; dazu wie bisher `defaultColor`, `defaultThickness`, `highlightColor`)
- Rückgängig/Wiederholen für alle Änderungen (Bereich, Objekte, Eigenschaften, Text, Maus); 200 Schritte
- Datenformat: Text hat das neue optionale Feld `width` (Umbruchbreite); weiterhin `formatVersion` 1
- Ein Render-Pfad: Anzeige = Export, Striche auf Pixelkanten ausgerichtet (1-px-Linien bleiben scharf)
- Tests für Bildeffekte, Treffertests, Geometrie (45°-Raster, Pfeilspitze, Glättung, Größe ändern), Rückgängig-Befehle
  (60 Schritte vor und zurück), Werkzeuge, Textbearbeitung, Optionen, Nummerierung und Serialisierung

## 0.2.0 – Phase 2: Editor (09.11.2026)

- Neuer Editor (ein Fenster für beide Modi): Vollbild 1:1 über alle Monitore (1 Bildpixel = 1 Gerätepixel) und
  Fenster-Modus (maximiert, Zoom 10–800 % mit Strg+Mausrad um den Mauspunkt, Verschieben mit Leertaste+Ziehen
  oder mittlerer Maustaste, Mausrad/Shift+Mausrad verschiebt)
- Bereich wählen: aufziehen, 8 Anfasser (auch die Kantenlinien), Verschieben im Bereich, neues Aufziehen außerhalb;
  Abdunkelung außerhalb (50 %), 1-px-Rahmen weiß/schwarz gestrichelt, Größenanzeige „B × H“; passende Mauszeiger
- Lupe (liest direkt aus dem Original): Vergrößerung aus den Einstellungen (4–16), Pixelraster, Fadenkreuz auf dem
  Mittelpixel, Anzeige Position · Größe · Farbwert (#RRGGBB) mit Farbfeld; weicht dem Cursor aus und bleibt im Monitor.
  Sichtbar vor der ersten Bereichswahl, beim Aufziehen, beim Ziehen der Anfasser und während der Tastatur-Auswahl
- Tastatur: Pfeiltasten bewegen Cursor bzw. aktive Kante um genau 1 Pixel (Shift: 10), der Mauszeiger wandert mit;
  Leertaste/Enter setzen Start- und Endpunkt (beide Pixel gehören zum Bereich)
- Schwebende Werkzeugleiste (unter dem Bereich, sonst darüber bzw. innen; vorher oben mittig auf dem primären Monitor):
  Bereich → Zwischenablage (Enter, Strg+C), Bereich speichern (Strg+S, PNG im Exportordner mit Kollisionssuffix),
  Speichern unter … (Strg+Shift+S, PNG oder JPG), ganzer Bildschirm → Zwischenablage (Strg+Shift+C),
  aktueller Monitor → Zwischenablage, Rückgängig/Wiederholen, Abbrechen (Esc). Bereich-Knöpfe ohne Bereich deaktiviert
- Rückgängig/Wiederholen (Strg+Z, Strg+Y bzw. Strg+Shift+Z) für alle Bereichsänderungen, 200 Schritte;
  ein Ziehvorgang = ein Schritt
- Esc bricht einen laufenden Ziehvorgang bzw. die Tastatur-Auswahl ab, sonst schließt es den Editor
- Bearbeitungsstand und Vorschaubild werden bei jedem Schließen gespeichert (Knöpfe, Esc, Alt+F4, WM_CLOSE)
- Editor kommt zuverlässig in den Vordergrund und erhält die Tastatur; Zeit vom Hotkey bis zur Anzeige im Protokoll
- Tests für Rückgängig-Stapel, Bereichs-Logik, Lupen- und Werkzeugleisten-Position, Umrechnung Maus → Bildpixel,
  Editor-Logik und Export-Dateinamen

## 0.1.0 – Phase 0 und 1 (09.11.2026)

### Phase 0 – Grundgerüst

- WPF-Projekt für .NET 10 (`net10.0-windows10.0.19041.0`), keine NuGet-Pakete
- Manifest mit `PerMonitorV2`, Windows 10/11
- Programmsymbol `ScreenKeeper.ico` (16–256 px), auch als Tray-Symbol
- Einzelinstanz je Benutzer (Mutex); ein zweiter Start öffnet die History der laufenden Instanz
- Tray-Symbol mit Kontextmenü (Aufnahme, History öffnen, Einstellungen, Beenden), Doppelklick = History
- History- und Einstellungsfenster als Platzhalter
- Alle Texte zentral in `Resources\Strings.resx` (Deutsch)
- Protokoll in `%APPDATA%\ScreenKeeper\log.txt`; unbehandelte Fehler werden protokolliert und gemeldet
- MVVM-Basisklassen (`ViewModelBase`, `RelayCommand`)

### Phase 1 – Aufnahme und Ablage

- Globale Hotkeys (Druck, Strg+Druck, Shift+Druck) über `RegisterHotKey`; ist die Druck-Taste belegt,
  Abfangen über einen Low-Level-Tastatur-Hook
- Hinweise beim Start: Windows-Einstellung „Druck-Taste öffnet Bildschirmausschnitt“, laufendes Greenshot/ShareX/Lightshot,
  nicht registrierbare Hotkeys
- Aufnahme des gesamten virtuellen Bildschirms ohne Mauszeiger (alle Monitore, auch mit negativen Koordinaten)
- Erfassung von Monitoren (Name, Bereich, primär, Skalierung), Mauszeiger (Bild mit Transparenz, Hotspot, sichtbar;
  auch monochrome und invertierende Zeiger) und sichtbaren Fenstern (Rahmen, Titel, Prozess, Z-Reihenfolge)
- Sofortiges Speichern: JSON und Vorschaubild direkt, Original-PNG im Hintergrund; alle Dateien werden atomar geschrieben
- Ablage in `<Ablageordner>\JJJJ\MM`, Dateinamen-Muster mit Platzhaltern, Kollisionssuffix `_2`, `_3` …
- Datenformat Version 1 mit allen Zeichenobjekt-Typen; unbekannte Typen und Felder bleiben beim Speichern erhalten
- Einstellungen in `%APPDATA%\ScreenKeeper\settings.json` (tolerant beim Laden)
- Aufgenommener Mauszeiger wird als virtuelle Maus ins Dokument übernommen
- Vorläufige Bildansicht 1:1 über alle Monitore (Esc schließt und speichert den Stand)
- Render-Pfad für Export (Original + virtuelle Maus, Zuschnitt auf Bereich/Monitor/Gesamt)
- Zwischenablage-Dienst (Bitmap + PNG), Autostart-Dienst, Löschen in den Papierkorb
- Automatisierte Tests (xUnit) für Datenformat, Ablage, Hotkeys, Einstellungen und Rendering
