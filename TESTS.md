# ScreenKeeper – Tests ausführen

Alle Befehle aus dem Repo-Root (Ordner mit `SPEC.md`). Voraussetzung: .NET SDK 10, Windows 10 (ab 2004) oder 11.
Für Aufnahmen muss der Desktop entsperrt sein (bei gesperrtem Bildschirm oder minimiertem Remotedesktop-Fenster
meldet `--capture` „Windows-Fehler 5“ und endet mit Exit-Code 1).

## 1. Build

```
dotnet build ScreenKeeper\ScreenKeeper.csproj -c Release --no-incremental
```

Erwartet: `0 Warning(s)`, `0 Error(s)` (bzw. `0 Warnung(en)`, `0 Fehler` bei deutscher SDK-Ausgabe). Die EXE liegt unter
`ScreenKeeper\bin\Release\net10.0-windows10.0.19041.0\ScreenKeeper.exe` (im Folgenden `ScreenKeeper.exe`).

## 2. Unit-Tests (xUnit)

```
dotnet test ScreenKeeper.Tests\ScreenKeeper.Tests.csproj
```

Erwartet: `Passed!  - Failed: 0, Passed: 335, Skipped: 0, Total: 335` (Stand 0.9.0). In einer getrennten
Remotedesktop-Sitzung scheitern die drei `RenderServiceTests` `Render_FullImageIsIdentical`,
`Render_DrawsCapturedCursorAtHotspot` und `Render_CutsRegionExactly` (kein Rendern ohne Bildschirm). Die Tests schreiben nur in
`%TEMP%\ScreenKeeperTests\…` und räumen danach auf; sie brauchen keinen entsperrten Desktop.

## 3. Headless-Testschalter

Mit einem dieser Schalter startet ScreenKeeper **ohne** Tray, Mutex und Hotkeys (läuft also auch neben einer
laufenden Instanz), schreibt Ergebnisse auf stdout und endet mit Exit-Code 0 (Erfolg) bzw. 1 (Fehler, Meldung auf
stderr). Die EXE ist eine Windows-Anwendung; damit PowerShell auf das Ende wartet und den Exit-Code sieht:

```powershell
$exe = ".\ScreenKeeper\bin\Release\net10.0-windows10.0.19041.0\ScreenKeeper.exe"
$p = Start-Process $exe -ArgumentList "--version" -Wait -PassThru -RedirectStandardOutput out.txt -RedirectStandardError err.txt
$p.ExitCode; Get-Content out.txt
```

Testordner (`<Ordner>`) am besten unter `%TEMP%` anlegen, damit nichts im echten Bilder-Ordner landet.

| Befehl | Was passiert | Erwartete Ausgabe |
|---|---|---|
| `--version` | Version ausgeben | `0.6.0` |
| `--capture --no-editor --folder <Ordner>` | echte Aufnahme aller Monitore, speichern | drei Zeilen: Pfade von `.png`, `.json`, `.thumb.jpg` unter `<Ordner>\JJJJ\MM\` |
| `--capture --folder <Ordner>` | wie oben, danach Vollbild-Editor (Esc schließt) | drei Pfade; Exit 0 nach dem Schließen |
| `--capture --test-capture-timing --folder <Ordner>` | Editor vorwärmen (wie der Tray-Betrieb beim Start), dann 3× Aufnahme + Editor wie per Hotkey, Zeit bis zum sichtbaren Editor messen, Editor schließen | `warmUpMs=…`, `run=1 visibleMs=…` … `run=3 …`, `min=… max=… warmAvg=…` (Ziel: alle < 300) |
| `--open <json>` | vorhandene Aufnahme im Editor öffnen (Vollbild bei gleicher Monitor-Anordnung, sonst Fenster) | Exit 0 nach dem Schließen; JSON neu geschrieben |
| `--open <json> --test-select x,y,w,h` | Bereich setzen, nach 1 s in die Zwischenablage kopieren, schließen | Exit 0; Log: `Zwischenablage: w×h bei x,y` |
| `--open <json> --test-annotate` | jedes der 11 Werkzeuge über echte Maus-Sequenzen bedienen, verschieben, vergrößern, 3× Rückgängig/Wiederholen, speichern | Exit 0; Log: `Test: 12 Objekte hinzugefügt, 14 Rückgängig-Schritte, Typen: …` (alle 11 Typen) |
| `--open <json> --test-magnifier` | Lupen-Zoom und -Größe über dieselben Wege wie Rad und +/- je einen Schritt ändern, Tastenhilfe prüfen, „Letzten Bereich“ mit einem Bereich in der Bildmitte übernehmen; speichert keine Einstellungen | Exit 0; `zoom=8->9 size=160->170 changed=True pendingSave=True`, `hints="…" hintsOk=True mode=…`, `lastSelection state=… applied=… selectionOk=True undoSteps=…` (Vollbild: `Available`/`True`/`1`, Fenster: `OtherLayout`/`False`/`0`) |
| `--render <json> <out.png> --region selection\|full\|monitor:<n>` | Dokument mit Zeichnungen rendern (derselbe Weg wie der Export) | Pfad von `out.png` |
| `--history --folder <Ordner> --test-history` | History öffnen, alle Einträge nachladen, Kennzahlen ausgeben, schließen | `entries=60 … firstBlockMs=…`, `total=… allMs=…`, `bindingErrors=0` |
| `--history --folder <Ordner> --test-edit <Name>` | Bearbeiten-Befehl der History, Editor nach 1 s schließen, Eintrag aktualisiert? | `edited=<Name> mode=… editorActive=True … hasJson=True reloaded=True` |
| `--history --folder <Ordner> --test-delete <Name>` | Aufnahme über den Löschen-Befehl der History in den Papierkorb | `deleted=<Name> before=… remaining=… filesLeft=0` |
| `--history --folder <Ordner> --test-duplicate <Name>` | Kopie über den Befehl der History anlegen | `duplicated=<Name> copy=<Name>_2 newFiles=3 … newIds=True originalUnchanged=True adjacent=True selected=True` |
| `--history --folder <Ordner> --test-permanent <Name>` | „Permanent“ umschalten, JSON prüfen, zurückschalten | `permanent=<Name> … json=True … restored=True ok=True` |
| `--retention --folder <Ordner> [--days <n>] [--dry-run]` | ein Aufräum-Lauf wie im Tray-Betrieb (Papierkorb) | `days=… expired=… keptPermanent=… filesLeft=0` |
| `--settings --test-settings` | alle Seiten des Einstellungsfensters laden, Kennzahlen, schließen ohne Speichern | `tabs=3 storage=… export=… preview=…`, `hotkeys=… status=… autostart=…`, `remote=True delay=3 ask=False after=edit`, `bindingErrors=0` |

Hinweise:

- `--test-annotate` immer auf einer **frischen** Aufnahme (bzw. Kopie) laufen lassen: Das Ergebnis wird gespeichert,
  ein zweiter Lauf auf derselben Datei trifft mit dem Text-Werkzeug den vorhandenen Text (öffnet ihn zum Bearbeiten
  statt ein neues Objekt anzulegen) und meldet dann nur 13 Schritte (Exit 1).
- `--test-select` überschreibt die Zwischenablage. Prüfen, dass Bitmap **und** PNG darin liegen:

  ```powershell
  Add-Type -AssemblyName System.Windows.Forms, System.Drawing
  (Get-Clipboard -Format Image).Size                                   # z. B. {Width=300, Height=200}
  $data = [Windows.Forms.Clipboard]::GetDataObject(); $data.GetFormats($false)   # Bitmap, PNG
  [Drawing.Image]::FromStream($data.GetData('PNG')).Size              # gleiche Größe, 32 bpp mit Alpha
  ```

- Für eine History mit vielen Einträgen genügen kleine PNGs mit passendem Namen (`JJJJ-MM-TT_HH-mm-ss.png` in
  `<Ordner>\JJJJ\MM\`); fehlende JSON-Dateien und Vorschaubilder sind erlaubt. Richtwerte bei 5.000 Aufnahmen:
  erster Block ca. 0,6 s, alle ca. 3,5 s.

## 4. Tray-Betrieb (manuell bzw. per Skript)

1. `ScreenKeeper.exe` ohne Argumente starten. Nach 1–3 s steht im Protokoll `%APPDATA%\ScreenKeeper\log.txt`:
   `ScreenKeeper 0.6.0 gestartet, Ablage: …`, je Hotkey `… registriert` oder `… nicht registrierbar (Fehler 1409) –
   Tastatur-Hook wird verwendet` und ggf. `Tastatur-Hook installiert für …`, danach `Editor vorgewärmt in … ms`.
2. Druck drücken → Vollbild-Editor im Vordergrund; Protokoll `Aufnahme … (Zeiger …, Bild …, Monitore/Fenster …)` und
   `Editor sichtbar nach … ms`. Esc → `Bearbeitungsstand gespeichert in … ms`.
3. Automatisiert lässt sich das mit `SendInput` für `VK_SNAPSHOT` (0x2C) nachstellen; der Editor wird über den
   Fenstertitel „ScreenKeeper – Bearbeiten“ gefunden und mit `PostMessage(WM_KEYDOWN, VK_ESCAPE)` geschlossen.
   Die Aufnahme landet im Standard-Ablageordner `Bilder\ScreenKeeper` – danach wieder löschen.
4. Beenden über Tray → *Beenden* (bei offenem Editor wird dessen Stand vorher gespeichert).

## 5. Manuelle Prüfung: Remotedesktop, verzögerte Aufnahme, Lupe, letzter Bereich

Nicht automatisierbar (braucht eine echte Remotedesktop-Verbindung bzw. Fokus und Bildschirm). Protokoll jeweils in
`%APPDATA%\ScreenKeeper\log.txt`.

**Hotkeys im Remotedesktop-Vollbild** (Einstellung „Hotkeys auch in Remotedesktop-Sitzungen abfangen“ an):

1. ScreenKeeper starten (nicht als Administrator), mstsc mit einer Sitzung im Vollbild öffnen. Protokoll:
   `Remotedesktop-Erfassung aktiv für Fensterklasse(n) TscShellContainerClass` und beim Aktivieren
   `Remotedesktop-Fenster (TscShellContainerClass) im Vordergrund – Tastatur-Hook wird neu gesetzt`.
2. Im Vollbild Druck, Strg+Druck, Shift+Druck drücken: jeweils genau eine Aktion (Editor bzw. gespeichert bzw.
   History), die Sitzung bekommt die Taste nicht.
3. Win, Alt+Tab, Strg+Alt+Ende, normales Tippen in der Sitzung: unverändert in der Sitzung.
4. Aus dem Vollbild zu einem lokalen Fenster wechseln (Protokoll `… nicht mehr im Vordergrund`), Hotkeys drücken:
   jeweils genau eine Aktion (kein doppelter Editor, keine doppelte Aufnahme).
5. Zurück ins Vollbild, Hotkeys erneut prüfen (Hook wird bei jeder Aktivierung neu gesetzt).
6. Einstellungen → Hotkeys, ein Hotkey-Feld anklicken (Aufzeichnung): Während der Aufzeichnung darf im RDP-Fenster
   nichts geschluckt werden; nach dem Verlassen des Felds wieder wie in 2.
7. Schalter aus → Übernehmen: Im Vollbild kommen die Hotkeys nicht mehr an (Verhalten wie vor 0.9).
8. mstsc als Administrator starten, ScreenKeeper nicht: Protokoll-Hinweis `… läuft mit Administratorrechten …`.
9. ScreenKeeper starten, während das RDP-Vollbild schon vorne ist: Protokoll `… bereits im Vordergrund …`, Hotkeys
   funktionieren sofort.

**Druck an Remotesitzung durchreichen** (z. B. Strg+Umschalt+Druck):

1. In der Sitzung ein Programm öffnen, das Druck sichtbar verarbeitet (z. B. Paint: nach Druck Strg+V).
2. Im RDP-Vollbild Strg+Umschalt+Druck: lokal keine Aufnahme, in der Sitzung landet ein Bildschirmfoto in deren
   Zwischenablage; Protokoll `Druck an die Remotesitzung durchgereicht (2 Zusatztaste(n) kurz losgelassen)`.
3. Zusatztasten danach weiter halten und eine andere Taste drücken: die Sitzung sieht sie weiterhin als gehalten.
4. Lokal (außerhalb RDP) Strg+Umschalt+Druck: geht unverändert an das lokale Programm (weder Aufnahme noch Durchreichen).
5. Gleiche Kombination wie ein anderer Hotkey eintragen → beim Speichern Fehler „… gleich …“.

**Verzögerte Aufnahme** (Hotkey z. B. Strg+Alt+D):

1. Feste Verzögerung 3 s: Hotkey, im Zielprogramm ein Menü aufklappen. Overlay rechts unten auf dem Monitor mit dem
   Mauszeiger zählt 3, 2, 1, Tray-Tooltip zeigt `Aufnahme in … s`; das Menü bleibt offen (kein Fokuswechsel), die
   Aufnahme zeigt das Menü, aber **nicht** das Overlay.
2. Esc während des Countdowns → Abbruch (Protokoll `Verzögerte Aufnahme abgebrochen (Esc)`); danach geht Esc wieder
   normal an die Programme. Ebenso Klick aufs Tray-Symbol, ebenso Druck (normale Aufnahme, Countdown bricht ab).
3. Hotkey während des Countdowns → sofort aufnehmen.
4. „Jedes Mal nach der Verzögerung fragen“ an: Dialog erscheint vorne, Wert vorbelegt; 5 s anklicken → Dialog weg,
   das vorherige Fenster hat wieder den Fokus, Countdown 5 s; beim nächsten Mal ist 5 vorbelegt. Enter bzw. der
   Hotkey im Dialog startet mit dem eingetragenen Wert, Esc bricht ab.
5. „Danach: Aufnahme nur speichern“: nach Ablauf Benachrichtigung statt Editor.
6. Editor offen → Hotkey holt nur den Editor nach vorne.
7. Im RDP-Vollbild: Hotkey und Esc funktionieren ebenso; die Aufnahme zeigt den lokalen Bildschirm mit dem RDP-Fenster.
8. Tray-Menü → *Verzögerte Aufnahme* startet mit der eingestellten Betriebsart.

**Lupe im Editor verstellen** (frische Aufnahme per Druck, Vollbild-Editor; Werkzeug „Bereich“, noch kein Bereich):

1. Unter der Lupe steht eine zweite, kleinere Zeile `Rad: Zoom 8×   +/-: Größe 160 px`.
2. Mausrad rauf/runter: Zoom ändert sich in ganzen Schritten (4–16), die Zeile zeigt den Wert, die Lupe folgt sofort
   ohne Mausbewegung. + / - (Haupttastatur und Ziffernblock): Größe in 10er-Schritten (80–400 px); in einer Ecke des
   Monitors ragt die vergrößerte Lupe nicht hinaus.
3. Etwa 1 s nach dem letzten Schritt Protokoll `Lupe: Zoom …, Größe … gespeichert` (einmal, nicht je Schritt);
   Einstellungen → Editor zeigt dieselben Werte. Ist das Einstellungsfenster dabei offen, zieht es die Werte nach;
   Übernehmen schreibt nicht die alten zurück.
4. Text-Werkzeug, Text mit + und - tippen: Zeichen landen im Text, die Lupe ändert sich nicht.
5. Aufnahme mit anderer Monitor-Anordnung aus der History (Fenster-Editor): Rad scrollt, Strg+Rad zoomt die Ansicht,
   **Alt+Rad** ändert den Lupen-Zoom; Zeile `Alt+Rad: …`; nach dem Loslassen von Alt ist das Fenstermenü nicht aktiv
   (der nächste Tastendruck wirkt normal).
6. Einstellungen → „Tastenhilfe unter der Lupe anzeigen“ aus → nächster Editor ohne die Zeile.

**Letzten Bereich** (Vollbild-Editor):

1. Bereich aufziehen, mit Enter kopieren (oder Esc). Protokoll `Letzter Bereich gemerkt: B×H bei X,Y (Bildschirm)`.
2. Neue Aufnahme: Knopf „Letzten Bereich“ (direkt nach den Werkzeugen) bzw. **Strg+L** setzt denselben Bereich an
   derselben Bildschirmstelle; Strg+Z nimmt ihn in einem Schritt zurück. `settings.json` enthält `lastSelection`.
3. „Aufnahme verwerfen“ mit geändertem Bereich: `lastSelection` bleibt unverändert.
4. Alte Aufnahme aus der History nur öffnen und schließen: `lastSelection` bleibt unverändert.
5. Fenster-Editor (andere Anordnung): Knopf ausgegraut, Tooltip nennt den Grund; ebenso ohne gemerkten Bereich.

## 6. Kodierung

Quelldateien `.cs`/`.xaml`: UTF-8 **mit** BOM, `.csproj`/`.resx`/`.manifest`/`.md`: UTF-8 ohne BOM; überall CRLF
(siehe `.editorconfig`). Prüfen z. B. mit einem kleinen Skript, das je Datei BOM und Zeilenenden vergleicht – erwartet
0 Abweichungen.
