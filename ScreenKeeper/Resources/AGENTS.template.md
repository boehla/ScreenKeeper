# ScreenKeeper-Ablageordner – Hinweise für KI-Agenten und Skripte

Geschrieben von ScreenKeeper {{VERSION}}. Diese Datei legt ScreenKeeper automatisch an und überschreibt sie, sobald sich
ihr Inhalt ändert (z. B. mit einer neuen Programmversion) – eigene Änderungen hier gehen verloren.

ScreenKeeper ist ein Screenshot-Tool für Windows. Jede Aufnahme fotografiert den **ganzen virtuellen Bildschirm**
(alle Monitore). Dieses Original-PNG wird nie verändert. Bereich, Zeichnungen, Kommentar und Metadaten stehen getrennt
davon in einer JSON-Datei daneben.

## Ordnerstruktur und Dateinamen

```
<Ablageordner>\
   AGENTS.md                       ← diese Datei
   <JJJJ>\<MM>\                    ← Jahr (4 Ziffern) und Monat (2 Ziffern) der Aufnahmezeit
      <Name>.png                   ← Original (verlustfrei, unverändert, ohne Mauszeiger)
      <Name>.json                  ← Metadaten und Bearbeitungsstand (siehe unten)
      <Name>.thumb.jpg             ← Vorschaubild für die History (JPEG, höchstens 320 px breit)
   Export\                         ← Standard-Exportordner (gerenderte Ausschnitte, frei benannt; kann umgestellt sein)
```

- `<Name>` entsteht aus einem einstellbaren Muster mit den Platzhaltern `{yyyy}` `{MM}` `{dd}` `{HH}` `{mm}` `{ss}`;
  Standard ist `{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}`, z. B. `2026-11-09_14-32-05`. Die Zeit ist die Uhrzeit bei der
  Aufnahme (lokale Zeit mit dem in `capturedAt` gespeicherten Offset). Ungültige Zeichen im Namen werden zu `_`.
- Bei Namensgleichheit (zwei Aufnahmen in derselben Sekunde, eine „Kopie anlegen“) wird `_2`, `_3` … angehängt, z. B.
  `2026-11-09_14-32-05_2.png`. Eine Kopie liegt im selben Ordner wie das Original, hat ein eigenes PNG (Byte-Kopie des
  Originals), eine eigene JSON mit neuen `id`s und ist nicht permanent.
- Zusammengehörige Dateien haben denselben Basisnamen. Ein PNG kann ohne JSON vorkommen (z. B. von Hand abgelegt);
  ScreenKeeper legt die JSON beim ersten Bearbeiten an. Vorschaubilder können fehlen.
- Kurzlebig bzw. als Sicherung können vorkommen: `<Datei>.tmp` (atomares Schreiben läuft gerade) und
  `<Name>.json.broken` (Kopie einer nicht lesbaren JSON, bevor sie neu angelegt wurde). Beides ist keine Aufnahme.
- Das Original-PNG ist **unverändert**: genau die aufgenommenen Bildschirmpixel, ohne Mauszeiger, ohne Bereich, ohne
  Zeichnungen. Zeichnungen, Bereich, virtuelle Maus und Kommentar stehen **nur in der JSON**. Das Vorschaubild
  (`.thumb.jpg`) und die Dateien im Exportordner sind dagegen gerendert (Bereich mit Zeichnungen).
- Andere Dateien und Ordner (auch diese AGENTS.md) ignoriert ScreenKeeper; gelesen werden nur `JJJJ\MM\*.png` mit der
  zugehörigen `.json`.

## Koordinatensystem

- Alle Koordinaten in der JSON sind ganzzahlige **Bildpixel des Original-PNGs**: (0, 0) ist die linke obere Ecke des
  PNGs, x nach rechts, y nach unten. Das PNG ist genau `virtualScreen.width` × `virtualScreen.height` Pixel groß.
- Einzige Ausnahme: `virtualScreen` selbst hält Lage und Größe des virtuellen Bildschirms in **Bildschirmkoordinaten**
  (physische Pixel, kann negativ sein) fest. Umrechnung: Bildschirm-x = Bild-x + `virtualScreen.x`, Bildschirm-y =
  Bild-y + `virtualScreen.y`.
- Ein Rechteck ist `{ "x": …, "y": …, "width": …, "height": … }`; rechte und untere Kante sind exklusiv
  (`x + width` liegt schon außerhalb).
- Ein Punkt ist ein Array `[x, y]` (gelesen wird auch `{ "x": …, "y": … }`).
- Farben sind Strings `#AARRGGBB` (Alpha zuerst, hexadezimal), z. B. `#FFE53935` = deckendes Rot, `#80FFEB3B` =
  halbtransparentes Gelb.
- Längen (Strichstärke, Schriftgröße, Durchmesser, Radius, Blockgröße) sind ebenfalls in Bildpixeln.

## Aufbau der JSON (`<Name>.json`)

UTF-8, eingerückt, Feldnamen in camelCase. Felder mit dem Wert `null` werden in der Regel weggelassen; fehlende Felder
haben den genannten Standardwert. Unbekannte Felder (z. B. aus neueren Versionen) bleiben beim Speichern erhalten.

| Feld | Typ | Bedeutung |
|---|---|---|
| `formatVersion` | Zahl | Formatversion, derzeit `1`. Neuere Versionen werden toleriert. |
| `capturedAt` | String | Aufnahmezeitpunkt ISO 8601 mit Offset, z. B. `2026-11-09T14:32:05+01:00` |
| `image` | String | Dateiname des Original-PNGs (ohne Ordner, relativ zur JSON) |
| `virtualScreen` | Rechteck | virtueller Bildschirm in Bildschirmkoordinaten (siehe oben) |
| `monitors` | Array | Monitore bei der Aufnahme (siehe unten) |
| `windows` | Array | sichtbare Top-Level-Fenster bei der Aufnahme (siehe unten) |
| `foreground` | Objekt | Programm im Vordergrund: `title` (Fenstertitel, kann leer sein), `process` (Prozessname ohne `.exe`); fehlt = unbekannt |
| `cursor` | Objekt | aufgenommener Mauszeiger (siehe unten); fehlt = keiner |
| `permanent` | bool | `true` = vom automatischen Aufräumen ausgenommen; nur geschrieben, wenn `true` (fehlt = `false`) |
| `comment` | String | **Kommentar** zur Aufnahme, freier mehrzeiliger Text (Zeilenumbruch `\r\n`); nur geschrieben, wenn nicht leer (fehlt = kein Kommentar). Die History durchsucht ihn. |
| `selection` | Rechteck | gewählter Bereich (Ausschnitt für Zwischenablage, Export, Vorschaubild); fehlt = kein Bereich, gemeint ist dann das ganze Bild |
| `annotations` | Array | Zeichenobjekte in Zeichenreihenfolge (das erste liegt unten), siehe nächster Abschnitt |
| `ocrText` | String | erkannter Text (spätere Ausbaustufe); wird immer geschrieben, derzeit `null` |

`monitors[]`:

| Feld | Typ | Bedeutung |
|---|---|---|
| `device` | String | Gerätename, z. B. `\\.\DISPLAY1`; leer bei Dokumenten, die ScreenKeeper für ein PNG ohne JSON angelegt hat |
| `bounds` | Rechteck | Monitorfläche in Bildpixeln |
| `primary` | bool | Hauptmonitor |
| `scale` | Zahl | Skalierung der Anzeige (1.0 = 100 %, 1.5 = 150 %) |

`windows[]` (für späteres Einrasten auf Fenster):

| Feld | Typ | Bedeutung |
|---|---|---|
| `title` | String | Fenstertitel |
| `process` | String | Prozessname ohne `.exe` |
| `bounds` | Rechteck | sichtbarer Fensterrahmen in Bildpixeln |
| `z` | Zahl | Z-Reihenfolge, 0 = oberstes Fenster |
| `foreground` | bool | dieses Fenster hatte den Fokus; nur geschrieben, wenn `true` |

`cursor`:

| Feld | Typ | Bedeutung |
|---|---|---|
| `png` | String | Zeigerbild als Base64-PNG mit Transparenz; fehlt = nicht ermittelbar |
| `x`, `y` | Zahl | Position des Hotspots in Bildpixeln |
| `hotspotX`, `hotspotY` | Zahl | Hotspot im Zeigerbild; das Bild wird bei (`x − hotspotX`, `y − hotspotY`) gezeichnet |
| `capturedVisible` | bool | war der Zeiger bei der Aufnahme sichtbar |

## Zeichenobjekte (`annotations[]`)

Jedes Objekt hat den Typ-Diskriminator `type` (wird als erstes Feld geschrieben) und `id` (String, Guid ohne
Bindestriche). Die Reihenfolge der Felder ist beim Lesen egal. Objekte mit unbekanntem `type`
werden unverändert aufbewahrt und nicht gezeichnet. Alle Zeichenobjekte sind nicht destruktiv: auch Verpixeln und
Weichzeichnen werden bei jeder Anzeige bzw. jedem Export neu aus dem Original berechnet.

Linienenden (`startCap`/`endCap`) haben die Werte `none`, `arrow`, `dot`, `square`, `triangle`. Fehlt das Feld oder ist
es `null`, gilt der Standard des Typs; unbekannte Werte werden ohne Symbol gezeichnet.

### `rectangle` – Rechteck

| Feld | Typ | Standard | Bedeutung |
|---|---|---|---|
| `rect` | Rechteck | | Lage und Größe |
| `stroke` | Farbe | `#FFE53935` | Rahmenfarbe |
| `thickness` | Zahl | `4` | Strichstärke |
| `fill` | Farbe | fehlt | Füllfarbe (Transparenz erlaubt); fehlt/`null`/`false` = keine Füllung, `true` (ältere Dateien) = in der Rahmenfarbe |

### `ellipse` – Ellipse

Ellipse innerhalb von `rect`; Felder wie `rectangle`: `rect`, `stroke`, `thickness`, `fill`.

### `line` – Linie

| Feld | Typ | Standard | Bedeutung |
|---|---|---|---|
| `points` | Punkte | | genau zwei: Anfang `[x, y]`, Ende `[x, y]` |
| `stroke` | Farbe | `#FFE53935` | Farbe |
| `thickness` | Zahl | `4` | Strichstärke |
| `startCap` | String | `none` | Symbol am Anfang |
| `endCap` | String | `none` | Symbol am Ende |

### `arrow` – Pfeil

Wie `line` (`points` mit Anfang und Spitze, `stroke`, `thickness`, `startCap`, `endCap`), aber Standard `startCap` =
`none` und `endCap` = `arrow`.

### `freehand` – Freihand

| Feld | Typ | Standard | Bedeutung |
|---|---|---|---|
| `points` | Punkte | | alle Punkte der Linie in Zeichenreihenfolge |
| `stroke` | Farbe | `#FFE53935` | Farbe |
| `thickness` | Zahl | `4` | Strichstärke |
| `smooth` | bool | `true` | Linie geglättet zeichnen |
| `startCap` | String | `none` | Symbol am ersten Punkt |
| `endCap` | String | `none` | Symbol am letzten Punkt |

### `text` – Text

| Feld | Typ | Standard | Bedeutung |
|---|---|---|---|
| `x`, `y` | Zahl | | linke obere Ecke des Textfelds |
| `text` | String | `""` | Text (mehrzeilig möglich) |
| `fontFamily` | String | `Segoe UI` | Schriftart |
| `fontSize` | Zahl | `24` | Schriftgröße in Bildpixeln |
| `bold` | bool | `false` | fett |
| `foreground` | Farbe | `#FFE53935` | Textfarbe |
| `background` | Farbe | fehlt | Hintergrundfarbe; fehlt = kein Hintergrund |
| `width` | Zahl | fehlt | Breite des Textfelds mit Zeilenumbruch; fehlt = ohne Umbruch |

### `highlight` – Textmarker

| Feld | Typ | Standard | Bedeutung |
|---|---|---|---|
| `rect` | Rechteck | | markierte Fläche |
| `color` | Farbe | `#FFFFEB3B` | Farbe; gezeichnet mit halbem Alpha-Wert der Farbe |

### `pixelate` – Verpixeln

| Feld | Typ | Standard | Bedeutung |
|---|---|---|---|
| `rect` | Rechteck | | verpixelte Fläche |
| `blockSize` | Zahl | `10` | Kantenlänge eines Blocks |

### `blur` – Weichzeichnen

| Feld | Typ | Standard | Bedeutung |
|---|---|---|---|
| `rect` | Rechteck | | weichgezeichnete Fläche |
| `radius` | Zahl | `8` | Stärke (Radius) |

### `number` – Nummern-Stempel

| Feld | Typ | Standard | Bedeutung |
|---|---|---|---|
| `x`, `y` | Zahl | | Mittelpunkt des Kreises |
| `number` | Zahl | `1` | angezeigte Zahl |
| `color` | Farbe | `#FFE53935` | Kreisfarbe |
| `size` | Zahl | `32` | Durchmesser |

### `cursor` – virtuelle Maus

| Feld | Typ | Standard | Bedeutung |
|---|---|---|---|
| `x`, `y` | Zahl | | Position des Hotspots |
| `kind` | String | `captured` | `captured` (Bild aus `cursor.png` der Aufnahme), `arrow`, `hand`, `ibeam` (Windows-Standardzeiger) |
| `visible` | bool | `true` | `false` = gespeichert, aber nicht gezeichnet |

War der Zeiger bei der Aufnahme sichtbar, enthält eine neue Aufnahme ein `cursor`-Objekt mit `kind` = `captured` an der
aufgenommenen Zeigerposition (`visible` nach der Einstellung „Aufgenommenen Mauszeiger standardmäßig anzeigen“); es zählt nicht als
Bearbeitung.

## Beispiel

```json
{
  "formatVersion": 1,
  "capturedAt": "2026-11-09T14:32:05+01:00",
  "image": "2026-11-09_14-32-05.png",
  "virtualScreen": { "x": -1920, "y": 0, "width": 3840, "height": 1080 },
  "monitors": [
    { "device": "\\\\.\\DISPLAY1", "bounds": { "x": 1920, "y": 0, "width": 1920, "height": 1080 }, "primary": true, "scale": 1.0 }
  ],
  "windows": [
    { "title": "Rechner", "process": "CalculatorApp", "bounds": { "x": 100, "y": 80, "width": 400, "height": 600 }, "z": 0, "foreground": true }
  ],
  "foreground": { "title": "Rechner", "process": "CalculatorApp" },
  "cursor": { "png": "<base64>", "x": 512, "y": 300, "hotspotX": 0, "hotspotY": 0, "capturedVisible": true },
  "permanent": true,
  "comment": "Fehlermeldung beim Speichern\r\nTicket 4711",
  "selection": { "x": 80, "y": 60, "width": 640, "height": 480 },
  "annotations": [
    { "type": "arrow", "points": [[100, 100], [220, 180]], "stroke": "#FFE53935", "thickness": 4, "startCap": "none", "endCap": "arrow", "id": "3f2a9c…" },
    { "type": "pixelate", "rect": { "x": 300, "y": 200, "width": 120, "height": 30 }, "blockSize": 10, "id": "8c1d47…" },
    { "type": "cursor", "x": 512, "y": 300, "kind": "captured", "visible": true, "id": "a7b0e2…" }
  ],
  "ocrText": null
}
```

## Hinweise zum Ändern

- Das PNG nie verändern; nur die JSON. Atomar schreiben (temporäre Datei daneben, dann umbenennen), UTF-8, unbekannte
  Felder erhalten.
- Ist dieselbe Aufnahme gerade im ScreenKeeper-Editor offen, überschreibt der Editor die JSON beim Schließen.
- Nach einer Änderung an Bereich oder Zeichnungen ist das Vorschaubild veraltet, bis die Aufnahme in ScreenKeeper
  bearbeitet wird (oder man `.thumb.jpg` löscht – fehlende Vorschaubilder erzeugt ScreenKeeper aus dem PNG neu).
