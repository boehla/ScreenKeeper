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

Erwartet: `Passed!  - Failed: 0, Passed: 205, Skipped: 0, Total: 205` (Stand 0.6.0). Die Tests schreiben nur in
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
| `--render <json> <out.png> --region selection\|full\|monitor:<n>` | Dokument mit Zeichnungen rendern (derselbe Weg wie der Export) | Pfad von `out.png` |
| `--history --folder <Ordner> --test-history` | History öffnen, alle Einträge nachladen, Kennzahlen ausgeben, schließen | `entries=60 … firstBlockMs=…`, `total=… allMs=…`, `bindingErrors=0` |
| `--history --folder <Ordner> --test-edit <Name>` | Bearbeiten-Befehl der History, Editor nach 1 s schließen, Eintrag aktualisiert? | `edited=<Name> mode=… editorActive=True … hasJson=True reloaded=True` |
| `--history --folder <Ordner> --test-delete <Name>` | Aufnahme über den Löschen-Befehl der History in den Papierkorb | `deleted=<Name> before=… remaining=… filesLeft=0` |
| `--history --folder <Ordner> --test-duplicate <Name>` | Kopie über den Befehl der History anlegen | `duplicated=<Name> copy=<Name>_2 newFiles=3 … newIds=True originalUnchanged=True adjacent=True selected=True` |
| `--history --folder <Ordner> --test-permanent <Name>` | „Permanent“ umschalten, JSON prüfen, zurückschalten | `permanent=<Name> … json=True … restored=True ok=True` |
| `--retention --folder <Ordner> [--days <n>] [--dry-run]` | ein Aufräum-Lauf wie im Tray-Betrieb (Papierkorb) | `days=… expired=… keptPermanent=… filesLeft=0` |
| `--settings --test-settings` | alle Seiten des Einstellungsfensters laden, Kennzahlen, schließen ohne Speichern | `tabs=3 storage=… export=… preview=…`, `hotkeys=… status=… autostart=…`, `bindingErrors=0` |

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

## 5. Kodierung

Quelldateien `.cs`/`.xaml`: UTF-8 **mit** BOM, `.csproj`/`.resx`/`.manifest`/`.md`: UTF-8 ohne BOM; überall CRLF
(siehe `.editorconfig`). Prüfen z. B. mit einem kleinen Skript, das je Datei BOM und Zeilenenden vergleicht – erwartet
0 Abweichungen.
