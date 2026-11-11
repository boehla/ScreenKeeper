using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services.Native;

namespace ScreenKeeper.Services;

/// <summary>
/// Die drei Dateien einer Aufnahme: Original-PNG, JSON und Vorschaubild.
/// </summary>
public sealed record CaptureFiles(string PngPath, string JsonPath, string ThumbPath) {
    public const string PngExtension = ".png";
    public const string JsonExtension = ".json";
    public const string ThumbExtension = ".thumb.jpg";

    public static CaptureFiles FromBase(string folder, string baseName) {
        string basePath = Path.Combine(folder, baseName);
        return new CaptureFiles(basePath + PngExtension, basePath + JsonExtension, basePath + ThumbExtension);
    }

    public static CaptureFiles FromPng(string pngPath) {
        string basePath = stripExtension(pngPath, PngExtension);
        return new CaptureFiles(pngPath, basePath + JsonExtension, basePath + ThumbExtension);
    }

    public static CaptureFiles FromJson(string jsonPath) {
        string basePath = stripExtension(jsonPath, JsonExtension);
        return new CaptureFiles(basePath + PngExtension, jsonPath, basePath + ThumbExtension);
    }

    /// <summary>Dateiname ohne <c>.png</c>, <c>.json</c> bzw. <c>.thumb.jpg</c>.</summary>
    public static string GetBaseName(string path) {
        string name = Path.GetFileName(path);
        foreach(string extension in new[] { ThumbExtension, PngExtension, JsonExtension }) {
            if(name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - extension.Length);
        }
        return name;
    }

    private static string stripExtension(string path, string extension) {
        return path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path.Substring(0, path.Length - extension.Length) : path;
    }
}

/// <summary>
/// Ergebnis von <see cref="IStorageService.SaveNewCapture"/>: Die JSON-Datei ist bereits geschrieben, das PNG
/// (<see cref="PngTask"/>) und das Vorschaubild (<see cref="ThumbnailTask"/>) werden im Hintergrund geschrieben.
/// </summary>
public sealed record SavedCapture(CaptureFiles Files, Task PngTask, Task ThumbnailTask);

/// <summary>
/// Eintrag für die History. <see cref="Time"/> ist die lokale Aufnahmezeit (aus dem Dateinamen, sonst das Änderungsdatum
/// des PNGs), <see cref="Size"/> die Größe des PNGs in Bytes.
/// </summary>
public sealed record CaptureEntry(CaptureFiles Files, DateTime Time, long Size = 0, bool HasJson = true, bool HasThumbnail = true) {
    /// <summary>Basisname ohne Endung, z. B. <c>2026-11-09_14-32-05</c>.</summary>
    public string Name => CaptureFiles.GetBaseName(Files.PngPath);

    /// <summary>Änderungszeit (UTC) der JSON-Datei beim Aufzählen; <see cref="DateTime.MinValue"/> = unbekannt.</summary>
    public DateTime JsonWriteTimeUtc { get; init; }

    /// <summary>
    /// Aus der JSON gelesene Kennzeichen (bearbeitet, permanent, Programm im Vordergrund); null = noch nicht gelesen.
    /// Wird von <see cref="IStorageService.GetMetadata"/> gesetzt, damit die JSON je Eintrag nur einmal gelesen wird.
    /// </summary>
    public CaptureMetadata? Metadata { get; set; }
}

/// <summary>
/// Ergebnis von <see cref="IStorageService.OpenOrCreate"/>: Dokument, Pfad der JSON-Datei und ob sie neu angelegt wurde.
/// </summary>
public sealed record OpenedCapture(CaptureDocument Document, string JsonPath, bool Created);

public interface IStorageService {
    /// <summary>Ersetzt den Ablageordner aus den Einstellungen (z. B. Kommandozeile <c>--folder</c>).</summary>
    string? RootFolderOverride { get; set; }

    string RootFolder { get; }

    string ExportFolder { get; }

    SavedCapture SaveNewCapture(CaptureDocument document, BitmapSource original);

    void SaveDocument(CaptureDocument document, string jsonPath);

    void UpdateThumbnail(CaptureDocument document, BitmapSource original, string jsonPath);

    CaptureDocument LoadDocument(string jsonPath);

    BitmapSource LoadOriginal(string pngPath);

    string ResolveImagePath(CaptureDocument document, string jsonPath);

    IEnumerable<CaptureEntry> EnumerateCaptures();

    /// <summary>
    /// Aufnahmen neueste zuerst; Monatsordner außerhalb von <paramref name="from"/> … <paramref name="to"/> (jeweils
    /// einschließlich) werden übersprungen. Die Einträge selbst filtert der Aufrufer.
    /// </summary>
    IEnumerable<CaptureEntry> EnumerateCaptures(DateTime? from, DateTime? to);

    /// <summary>History-Eintrag zu den Dateien einer Aufnahme (z. B. nach einer neuen Aufnahme); null, wenn das PNG fehlt.</summary>
    CaptureEntry? GetEntry(CaptureFiles files);

    /// <summary>
    /// Dokument zu einem PNG oder einer JSON-Datei laden. Fehlt die JSON-Datei (oder ist sie defekt), wird ein neues Dokument
    /// angelegt und gespeichert (ein Monitor in Bildgröße).
    /// </summary>
    OpenedCapture OpenOrCreate(string pngOrJsonPath);

    /// <summary>Erzeugt das Vorschaubild aus dem PNG, falls es fehlt. True, wenn es danach vorhanden ist.</summary>
    bool EnsureThumbnail(CaptureFiles files);

    /// <summary>
    /// Kennzeichen aus der JSON des Eintrags (einmal gelesen, danach aus <see cref="CaptureEntry.Metadata"/> bzw. einem
    /// Zwischenspeicher, solange sich die Datei nicht ändert). Ohne JSON: <see cref="CaptureMetadata.None"/>. Threadsicher.
    /// </summary>
    CaptureMetadata GetMetadata(CaptureEntry entry);

    /// <summary>
    /// Legt eine Kopie der Aufnahme (PNG, JSON, Vorschaubild) im selben Ordner an: Basisname mit Suffix <c>_2</c>,
    /// <c>_3</c> …, neue Ids für alle Zeichenobjekte, nicht permanent. Liefert die Dateien der Kopie.
    /// </summary>
    CaptureFiles DuplicateCapture(string pngOrJsonPath);

    /// <summary>
    /// Markierung „permanent“ setzen und die JSON speichern (fehlt sie, wird sie wie beim Bearbeiten angelegt).
    /// </summary>
    CaptureDocument SetPermanent(string pngOrJsonPath, bool permanent);

    /// <summary>
    /// Kommentar setzen (leer = entfernen) und die JSON atomar speichern (fehlt sie, wird sie wie beim Bearbeiten angelegt).
    /// </summary>
    CaptureDocument SetComment(string pngOrJsonPath, string? comment);

    /// <summary>
    /// Legt die <see cref="AgentsFile.FileName"/> im Ablageordner an bzw. aktualisiert sie, falls sie fehlt oder veraltet
    /// ist (je Ablageordner einmal je Programmlauf). Nur, wenn der Ablageordner existiert. Fehler werden nur protokolliert.
    /// </summary>
    void EnsureAgentsFile();

    void DeleteToRecycleBin(CaptureFiles files);

    /// <summary>Verschiebt alle Dateien mehrerer Aufnahmen in einem Vorgang in den Papierkorb.</summary>
    void DeleteToRecycleBin(IEnumerable<CaptureFiles> captures);
}

/// <summary>
/// Ablage der Aufnahmen: &lt;Ablageordner&gt;\JJJJ\MM\&lt;Name&gt;.png/.json/.thumb.jpg.
/// </summary>
public sealed class StorageService : IStorageService {
    public const int ThumbnailMaxWidth = 320;
    private const int thumbnailQuality = 85;
    private static readonly TimeSpan pendingWriteTimeout = TimeSpan.FromSeconds(30);

    private readonly ISettingsService settings;
    private readonly IRenderService render;
    // Laufende Hintergrund-Schreibvorgänge je Datei (voller Pfad), damit spätere Schreib- oder Löschvorgänge darauf warten
    private readonly ConcurrentDictionary<string, Task> pendingWrites = new ConcurrentDictionary<string, Task>(StringComparer.OrdinalIgnoreCase);
    // Gelesene Kennzeichen je JSON-Datei mit deren Änderungszeit (UTC), damit Filter und History sie nicht erneut lesen
    private readonly ConcurrentDictionary<string, (DateTime Stamp, CaptureMetadata Metadata)> metadataCache = new ConcurrentDictionary<string, (DateTime Stamp, CaptureMetadata Metadata)>(StringComparer.OrdinalIgnoreCase);
    private readonly object agentsSync = new object();
    // Ablageordner, für den die AGENTS.md in diesem Programmlauf schon geprüft wurde
    private string? agentsCheckedRoot;

    public StorageService(ISettingsService settings, IRenderService render) {
        this.settings = settings;
        this.render = render;
    }

    public string? RootFolderOverride { get; set; }

    public string RootFolder {
        get {
            if(!string.IsNullOrWhiteSpace(RootFolderOverride)) return Path.GetFullPath(RootFolderOverride);
            return ResolveStorageFolder(settings.Settings.StorageFolder);
        }
    }

    public string ExportFolder => ResolveExportFolder(settings.Settings.ExportFolder, RootFolder);

    /// <summary>Ablageordner aus der Einstellung (Umgebungsvariablen erlaubt); leer = Bilder\ScreenKeeper.</summary>
    public static string ResolveStorageFolder(string? configured) {
        if(!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim()));
        return AppPaths.DefaultStorageFolder;
    }

    /// <summary>Exportordner aus der Einstellung; leer = &lt;Ablageordner&gt;\Export.</summary>
    public static string ResolveExportFolder(string? configured, string rootFolder) {
        if(!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim()));
        return Path.Combine(rootFolder, "Export");
    }

    public SavedCapture SaveNewCapture(CaptureDocument document, BitmapSource original) {
        // Uhrzeit so, wie sie bei der Aufnahme auf der Uhr stand (mit dem gespeicherten Offset), unabhängig von der
        // Zeitzone des Rechners, der die Datei gerade anlegt
        DateTime time = document.CapturedAt.DateTime;
        string folder = Path.Combine(RootFolder, time.ToString("yyyy", CultureInfo.InvariantCulture), time.ToString("MM", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        // Nur beim ersten Mal je Ablageordner ein kurzer Dateivergleich; Fehler werden nur protokolliert
        EnsureAgentsFile();
        string baseName = GetUniqueBaseName(folder, FormatFileName(settings.Settings.FileNamePattern, time));
        CaptureFiles files = CaptureFiles.FromBase(folder, baseName);
        document.Image = Path.GetFileName(files.PngPath);

        // JSON sofort, damit Name und Metadaten auch bei einem Absturz gesichert sind. Vorschaubild und PNG im
        // Hintergrund (das Bild ist eingefroren), damit der Editor ohne Wartezeit erscheint
        SaveDocument(document, files.JsonPath);
        Task thumbnailTask = startWrite(files.ThumbPath, () => WriteThumbnail(original, files.ThumbPath));
        Task pngTask = startWrite(files.PngPath, () => ImageFiles.WritePng(original, files.PngPath));
        return new SavedCapture(files, pngTask, thumbnailTask);
    }

    public void SaveDocument(CaptureDocument document, string jsonPath) {
        CaptureDocumentSerializer.Save(document, jsonPath);
        // Kennzeichen (z. B. Kommentar) beim nächsten Zugriff neu lesen, auch wenn sich die Änderungszeit nicht messbar ändert
        metadataCache.TryRemove(Path.GetFullPath(jsonPath), out _);
    }

    /// <summary>
    /// Vorschaubild neu erzeugen: gewählter Bereich mit Zeichnungen, ohne Bereich das ganze Bild.
    /// </summary>
    public void UpdateThumbnail(CaptureDocument document, BitmapSource original, string jsonPath) {
        PixelRect region = document.Selection ?? new PixelRect(0, 0, original.PixelWidth, original.PixelHeight);
        BitmapSource rendered = render.Render(document, original, region);
        string thumbPath = CaptureFiles.FromJson(jsonPath).ThumbPath;
        // Das erste Vorschaubild einer neuen Aufnahme darf das aktuelle nicht überholen
        waitForPendingWrite(thumbPath);
        WriteThumbnail(rendered, thumbPath);
    }

    public CaptureDocument LoadDocument(string jsonPath) {
        return CaptureDocumentSerializer.Load(jsonPath);
    }

    public BitmapSource LoadOriginal(string pngPath) {
        return ImageFiles.LoadBgra32(pngPath);
    }

    /// <summary>
    /// Pfad des Original-PNGs: <see cref="CaptureDocument.Image"/> relativ zur JSON-Datei, sonst gleicher Basisname.
    /// </summary>
    public string ResolveImagePath(CaptureDocument document, string jsonPath) {
        string folder = Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? "";
        if(!string.IsNullOrWhiteSpace(document.Image)) return Path.Combine(folder, Path.GetFileName(document.Image));
        return CaptureFiles.FromJson(jsonPath).PngPath;
    }

    /// <summary>
    /// Alle Aufnahmen, neueste zuerst. Lazy: Ordner werden erst beim Weiterzählen gelesen.
    /// </summary>
    public IEnumerable<CaptureEntry> EnumerateCaptures() {
        return EnumerateCaptures(null, null);
    }

    /// <summary>
    /// Aufnahmen neueste zuerst. Die Monatsordner <c>JJJJ\MM</c> werden von neu nach alt gelesen (jeweils komplett, dann
    /// nach Zeit sortiert), damit die ersten Einträge sofort da sind. Ordner außerhalb des Zeitraums werden übersprungen.
    /// </summary>
    public IEnumerable<CaptureEntry> EnumerateCaptures(DateTime? from, DateTime? to) {
        string root = RootFolder;
        if(!Directory.Exists(root)) yield break;
        string pattern = settings.Settings.FileNamePattern;
        int? fromMonth = from.HasValue ? from.Value.Year * 12 + from.Value.Month : null;
        int? toMonth = to.HasValue ? to.Value.Year * 12 + to.Value.Month : null;
        foreach(string yearFolder in getNumericFolders(root, 4)) {
            int year = int.Parse(Path.GetFileName(yearFolder), CultureInfo.InvariantCulture);
            // Jahre absteigend: ein Jahr vor dem Zeitraum beendet die Suche
            if(fromMonth.HasValue && year * 12 + 12 < fromMonth.Value) yield break;
            if(toMonth.HasValue && year * 12 + 1 > toMonth.Value) continue;
            foreach(string monthFolder in getNumericFolders(yearFolder, 2)) {
                int month = year * 12 + int.Parse(Path.GetFileName(monthFolder), CultureInfo.InvariantCulture);
                if(fromMonth.HasValue && month < fromMonth.Value) yield break;
                if(toMonth.HasValue && month > toMonth.Value) continue;
                foreach(CaptureEntry entry in ReadFolder(monthFolder, pattern)) yield return entry;
            }
        }
    }

    /// <summary>
    /// Alle Aufnahmen (PNGs) eines Ordners, neueste zuerst. Ein einziger Verzeichnisdurchlauf liefert auch, ob JSON und
    /// Vorschaubild vorhanden sind. Zeit aus dem Dateinamen (Muster, sonst Standardmuster), sonst Änderungsdatum des PNGs.
    /// </summary>
    public static List<CaptureEntry> ReadFolder(string folder, string pattern) {
        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, FileInfo> jsons = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        List<FileInfo> pngs = new List<FileInfo>();
        foreach(FileInfo file in new DirectoryInfo(folder).EnumerateFiles()) {
            names.Add(file.Name);
            if(file.Name.EndsWith(CaptureFiles.PngExtension, StringComparison.OrdinalIgnoreCase)) {
                pngs.Add(file);
            } else if(file.Name.EndsWith(CaptureFiles.JsonExtension, StringComparison.OrdinalIgnoreCase)) {
                jsons[file.Name] = file;
            }
        }
        List<CaptureEntry> entries = new List<CaptureEntry>(pngs.Count);
        foreach(FileInfo png in pngs) {
            string baseName = png.Name.Substring(0, png.Name.Length - CaptureFiles.PngExtension.Length);
            DateTime time = GetCaptureTime(baseName, pattern, png.LastWriteTime);
            bool hasJson = jsons.TryGetValue(baseName + CaptureFiles.JsonExtension, out FileInfo? json);
            bool hasThumb = names.Contains(baseName + CaptureFiles.ThumbExtension);
            entries.Add(new CaptureEntry(CaptureFiles.FromPng(png.FullName), time, png.Length, hasJson, hasThumb) {
                // Zeit aus demselben Verzeichnisdurchlauf, ohne zusätzlichen Dateizugriff
                JsonWriteTimeUtc = json != null ? json.LastWriteTimeUtc : DateTime.MinValue
            });
        }
        entries.Sort(CompareNewestFirst);
        return entries;
    }

    /// <summary>Reihenfolge der History: neueste zuerst, bei gleicher Zeit der größere Name (Suffix <c>_2</c>) zuerst.</summary>
    public static int CompareNewestFirst(CaptureEntry a, CaptureEntry b) {
        int result = b.Time.CompareTo(a.Time);
        return result != 0 ? result : StringComparer.OrdinalIgnoreCase.Compare(b.Name, a.Name);
    }

    /// <summary>
    /// Aufnahmezeit aus dem Basisnamen (Muster, dann Standardmuster); sonst <paramref name="fallback"/>.
    /// </summary>
    public static DateTime GetCaptureTime(string baseName, string pattern, DateTime fallback) {
        if(CaptureFileName.TryParseTime(baseName, pattern, out DateTime time)) return time;
        if(pattern != AppSettings.DefaultFileNamePattern && CaptureFileName.TryParseTime(baseName, AppSettings.DefaultFileNamePattern, out time)) return time;
        return fallback;
    }

    public CaptureEntry? GetEntry(CaptureFiles files) {
        FileInfo png = new FileInfo(files.PngPath);
        if(!png.Exists) return null;
        DateTime time = GetCaptureTime(CaptureFiles.GetBaseName(files.PngPath), settings.Settings.FileNamePattern, png.LastWriteTime);
        FileInfo json = new FileInfo(files.JsonPath);
        return new CaptureEntry(files, time, png.Length, json.Exists, File.Exists(files.ThumbPath)) {
            JsonWriteTimeUtc = json.Exists ? json.LastWriteTimeUtc : DateTime.MinValue
        };
    }

    public CaptureMetadata GetMetadata(CaptureEntry entry) {
        CaptureMetadata? known = entry.Metadata;
        if(known != null) return known;
        CaptureMetadata metadata = CaptureMetadata.None;
        if(entry.HasJson) {
            string key = Path.GetFullPath(entry.Files.JsonPath);
            DateTime stamp = entry.JsonWriteTimeUtc != DateTime.MinValue ? entry.JsonWriteTimeUtc : File.GetLastWriteTimeUtc(key);
            if(metadataCache.TryGetValue(key, out (DateTime Stamp, CaptureMetadata Metadata) cached) && cached.Stamp == stamp) {
                metadata = cached.Metadata;
            } else {
                try {
                    metadata = CaptureMetadata.FromDocument(LoadDocument(key));
                } catch(Exception ex) {
                    Log.Warn($"Dokument {key} nicht lesbar: {ex.Message}");
                    metadata = CaptureMetadata.Broken;
                }
                metadataCache[key] = (stamp, metadata);
            }
        }
        entry.Metadata = metadata;
        return metadata;
    }

    public CaptureFiles DuplicateCapture(string pngOrJsonPath) {
        CaptureFiles source = filesFor(pngOrJsonPath);
        // Ein laufendes Hintergrund-Schreiben (neue Aufnahme) zuerst abschließen, sonst würde eine halbe Datei kopiert
        foreach(string path in new[] { source.PngPath, source.JsonPath, source.ThumbPath }) waitForPendingWrite(path);
        string pattern = settings.Settings.FileNamePattern;
        CaptureDocument document;
        string pngPath = source.PngPath;
        if(File.Exists(source.JsonPath)) {
            document = LoadDocument(source.JsonPath);
            pngPath = ResolveImagePath(document, source.JsonPath);
        } else {
            if(!File.Exists(pngPath)) throw new FileNotFoundException(Strings.Format(Strings.ErrorImageNotFound, pngPath), pngPath);
            document = CreateDocumentForImage(pngPath, pattern);
        }
        if(!File.Exists(pngPath)) throw new FileNotFoundException(Strings.Format(Strings.ErrorImageNotFound, pngPath), pngPath);

        string folder = Path.GetDirectoryName(source.JsonPath) ?? "";
        string baseName = GetUniqueBaseName(folder, GetCopyStem(CaptureFiles.GetBaseName(source.JsonPath), pattern));
        CaptureFiles copy = CaptureFiles.FromBase(folder, baseName);
        PrepareCopy(document, Path.GetFileName(copy.PngPath));
        List<string> created = new List<string>();
        try {
            File.Copy(pngPath, copy.PngPath, false);
            created.Add(copy.PngPath);
            if(File.Exists(source.ThumbPath)) {
                File.Copy(source.ThumbPath, copy.ThumbPath, false);
                created.Add(copy.ThumbPath);
            }
            SaveDocument(document, copy.JsonPath);
            created.Add(copy.JsonPath);
        } catch(Exception ex) {
            // keine halbe Kopie liegen lassen
            Log.Error(ex, $"Kopie von {source.JsonPath} fehlgeschlagen");
            foreach(string path in created) deleteQuietly(path);
            throw;
        }
        if(!File.Exists(copy.ThumbPath)) {
            try {
                EnsureThumbnail(copy);
            } catch(Exception ex) {
                Log.Error(ex, $"Vorschaubild für {copy.PngPath} konnte nicht erzeugt werden");
            }
        }
        Log.Info($"Kopie angelegt: {source.JsonPath} → {copy.JsonPath} ({document.Annotations.Count} Zeichenobjekt(e))");
        return copy;
    }

    /// <summary>
    /// Dokument für eine Kopie vorbereiten: Bildname der Kopie, neue Ids für alle Zeichenobjekte (auch unbekannte Typen),
    /// nicht permanent.
    /// </summary>
    public static void PrepareCopy(CaptureDocument document, string imageName) {
        document.Image = imageName;
        document.Permanent = false;
        for(int i = 0; i < document.Annotations.Count; i++) {
            Annotation annotation = document.Annotations[i];
            string id = Guid.NewGuid().ToString("N");
            if(annotation is UnknownAnnotation unknown) {
                if(unknown.Raw.ValueKind != JsonValueKind.Object) continue;
                JsonObject? raw = JsonNode.Parse(unknown.Raw.GetRawText()) as JsonObject;
                if(raw == null) continue;
                raw["id"] = id;
                document.Annotations[i] = new UnknownAnnotation(JsonSerializer.SerializeToElement(raw));
                continue;
            }
            annotation.Id = id;
        }
    }

    /// <summary>
    /// Basisname, an den die Kopie ihr Suffix hängt: ein vorhandenes Kollisionssuffix (<c>_2</c>) wird entfernt, wenn der
    /// Rest ein Aufnahme-Name ist – so heißt die Kopie von <c>…_05_2</c> <c>…_05_3</c> statt <c>…_05_2_2</c>.
    /// </summary>
    public static string GetCopyStem(string baseName, string pattern) {
        Match suffix = Regex.Match(baseName, @"^(.+)_\d+$");
        if(!suffix.Success) return baseName;
        string stem = suffix.Groups[1].Value;
        return GetCaptureTime(stem, pattern, DateTime.MinValue) != DateTime.MinValue ? stem : baseName;
    }

    public CaptureDocument SetPermanent(string pngOrJsonPath, bool permanent) {
        OpenedCapture opened = OpenOrCreate(pngOrJsonPath);
        if(opened.Document.Permanent != permanent) {
            opened.Document.Permanent = permanent;
            SaveDocument(opened.Document, opened.JsonPath);
        }
        Log.Info($"Aufnahme {opened.JsonPath} {(permanent ? "als permanent markiert" : "nicht mehr permanent")}");
        return opened.Document;
    }

    public CaptureDocument SetComment(string pngOrJsonPath, string? comment) {
        OpenedCapture opened = OpenOrCreate(pngOrJsonPath);
        string? normalized = CaptureDocument.NormalizeComment(comment);
        if(opened.Document.Comment != normalized) {
            opened.Document.Comment = normalized;
            SaveDocument(opened.Document, opened.JsonPath);
        }
        Log.Info($"Aufnahme {opened.JsonPath}: Kommentar {(normalized == null ? "entfernt" : $"gesetzt ({normalized.Length} Zeichen)")}");
        return opened.Document;
    }

    public void EnsureAgentsFile() {
        string root;
        try {
            root = RootFolder;
        } catch(Exception ex) {
            Log.Warn($"AGENTS.md: Ablageordner nicht bestimmbar: {ex.Message}");
            return;
        }
        lock(agentsSync) {
            if(string.Equals(agentsCheckedRoot, root, StringComparison.OrdinalIgnoreCase)) return;
            if(!Directory.Exists(root)) return;
            AgentsFile.TryEnsure(root);
            // auch nach einem Fehler nicht bei jeder Aufnahme erneut versuchen (nächster Start bzw. Ordnerwechsel)
            agentsCheckedRoot = root;
        }
    }

    public OpenedCapture OpenOrCreate(string pngOrJsonPath) {
        CaptureFiles files = filesFor(pngOrJsonPath);
        if(File.Exists(files.JsonPath)) {
            try {
                return new OpenedCapture(LoadDocument(files.JsonPath), files.JsonPath, false);
            } catch(Exception ex) {
                // Defekte JSON nicht verlieren: daneben aufheben, dann neu anlegen
                Log.Error(ex, $"Dokument {files.JsonPath} ist nicht lesbar – es wird neu angelegt");
                File.Copy(files.JsonPath, files.JsonPath + ".broken", true);
            }
        }
        if(!File.Exists(files.PngPath)) throw new FileNotFoundException(Strings.Format(Strings.ErrorImageNotFound, files.PngPath), files.PngPath);
        CaptureDocument document = CreateDocumentForImage(files.PngPath, settings.Settings.FileNamePattern);
        SaveDocument(document, files.JsonPath);
        try {
            EnsureThumbnail(files);
        } catch(Exception ex) {
            Log.Error(ex, $"Vorschaubild für {files.PngPath} konnte nicht erzeugt werden");
        }
        Log.Info($"Neues Dokument für {files.PngPath} angelegt ({document.VirtualScreen.Width}×{document.VirtualScreen.Height})");
        return new OpenedCapture(document, files.JsonPath, true);
    }

    /// <summary>
    /// Neues Dokument für ein PNG ohne JSON: virtueller Bildschirm = Bildgröße ab 0,0, ein primärer Monitor mit Skalierung 1.
    /// </summary>
    public static CaptureDocument CreateDocumentForImage(string pngPath, string pattern) {
        (int width, int height) = ImageFiles.ReadSize(pngPath);
        PixelRect bounds = new PixelRect(0, 0, width, height);
        DateTime time = GetCaptureTime(CaptureFiles.GetBaseName(pngPath), pattern, File.GetLastWriteTime(pngPath));
        time = DateTime.SpecifyKind(time.AddTicks(-(time.Ticks % TimeSpan.TicksPerSecond)), DateTimeKind.Local);
        return new CaptureDocument {
            CapturedAt = new DateTimeOffset(time),
            Image = Path.GetFileName(pngPath),
            VirtualScreen = bounds,
            Monitors = new List<MonitorInfo> {
                new MonitorInfo { Device = "", Bounds = bounds, Primary = true, Scale = 1.0 }
            }
        };
    }

    public bool EnsureThumbnail(CaptureFiles files) {
        waitForPendingWrite(files.ThumbPath);
        if(File.Exists(files.ThumbPath)) return true;
        if(!File.Exists(files.PngPath)) return false;
        WriteThumbnail(ImageFiles.LoadScaled(files.PngPath, ThumbnailMaxWidth), files.ThumbPath);
        return true;
    }

    public void DeleteToRecycleBin(CaptureFiles files) {
        DeleteToRecycleBin(new[] { files });
    }

    /// <summary>
    /// Verschiebt alle vorhandenen Dateien der Aufnahmen (PNG, JSON, Vorschaubild) in einem Vorgang in den Papierkorb.
    /// </summary>
    public void DeleteToRecycleBin(IEnumerable<CaptureFiles> captures) {
        List<string> existing = new List<string>();
        foreach(CaptureFiles files in captures) {
            foreach(string path in new[] { files.PngPath, files.JsonPath, files.ThumbPath }) {
                // sonst entstünde die Datei nach dem Löschen neu bzw. die .tmp-Datei bliebe liegen
                waitForPendingWrite(path);
                if(File.Exists(path)) existing.Add(Path.GetFullPath(path));
            }
        }
        if(existing.Count == 0) return;
        SHFILEOPSTRUCT operation = new SHFILEOPSTRUCT {
            wFunc = Shell32.FO_DELETE,
            pFrom = string.Join("\0", existing) + "\0\0",
            fFlags = (ushort)(Shell32.FOF_ALLOWUNDO | Shell32.FOF_NOCONFIRMATION | Shell32.FOF_SILENT)
        };
        int result = Shell32.SHFileOperation(ref operation);
        if(result != 0) throw new IOException($"Löschen in den Papierkorb fehlgeschlagen (Code {result}).");
        if(operation.fAnyOperationsAborted) throw new IOException("Löschen in den Papierkorb wurde abgebrochen.");
    }

    /// <summary>
    /// Ersetzt die Platzhalter {yyyy} {MM} {dd} {HH} {mm} {ss} und entfernt ungültige Zeichen.
    /// </summary>
    public static string FormatFileName(string pattern, DateTime time) {
        if(string.IsNullOrWhiteSpace(pattern)) pattern = AppSettings.DefaultFileNamePattern;
        StringBuilder name = new StringBuilder(pattern);
        name.Replace("{yyyy}", time.ToString("yyyy", CultureInfo.InvariantCulture));
        name.Replace("{MM}", time.ToString("MM", CultureInfo.InvariantCulture));
        name.Replace("{dd}", time.ToString("dd", CultureInfo.InvariantCulture));
        name.Replace("{HH}", time.ToString("HH", CultureInfo.InvariantCulture));
        name.Replace("{mm}", time.ToString("mm", CultureInfo.InvariantCulture));
        name.Replace("{ss}", time.ToString("ss", CultureInfo.InvariantCulture));
        char[] invalid = Path.GetInvalidFileNameChars();
        for(int i = 0; i < name.Length; i++) {
            if(Array.IndexOf(invalid, name[i]) >= 0) name[i] = '_';
        }
        string result = name.ToString().Trim().TrimEnd('.');
        return result.Length == 0 ? FormatFileName(AppSettings.DefaultFileNamePattern, time) : result;
    }

    /// <summary>
    /// Liefert <paramref name="baseName"/> oder bei Namensgleichheit <c>_2</c>, <c>_3</c> … angehängt.
    /// </summary>
    public static string GetUniqueBaseName(string folder, string baseName) {
        string candidate = baseName;
        int counter = 2;
        while(isTaken(folder, candidate)) {
            candidate = $"{baseName}_{counter}";
            counter++;
        }
        return candidate;
    }

    /// <summary>
    /// Verkleinert auf höchstens <see cref="ThumbnailMaxWidth"/> Pixel Breite (eingefroren).
    /// </summary>
    public static BitmapSource CreateThumbnail(BitmapSource source) {
        if(source.PixelWidth <= ThumbnailMaxWidth) return source;
        double scale = (double)ThumbnailMaxWidth / source.PixelWidth;
        BitmapSource result = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        if(result.PixelWidth > ThumbnailMaxWidth) {
            result = new CroppedBitmap(result, new Int32Rect(0, 0, ThumbnailMaxWidth, result.PixelHeight));
        }
        result.Freeze();
        return result;
    }

    public static void WriteThumbnail(BitmapSource source, string thumbPath) {
        ImageFiles.WriteJpeg(CreateThumbnail(source), thumbPath, thumbnailQuality);
    }

    private Task startWrite(string path, Action write) {
        string key = Path.GetFullPath(path);
        Task task = Task.Run(write);
        pendingWrites[key] = task;
        task.ContinueWith(done => pendingWrites.TryRemove(new KeyValuePair<string, Task>(key, done)), TaskScheduler.Default);
        return task;
    }

    /// <summary>
    /// Wartet auf ein laufendes Hintergrund-Schreiben der Datei. Dessen Fehler meldet der Aufrufer von
    /// <see cref="SaveNewCapture"/>; hier wird er nur protokolliert.
    /// </summary>
    private void waitForPendingWrite(string path) {
        if(!pendingWrites.TryGetValue(Path.GetFullPath(path), out Task? task)) return;
        try {
            if(!task.Wait(pendingWriteTimeout)) Log.Warn($"Hintergrund-Schreiben von {path} dauert zu lange");
        } catch(AggregateException ex) {
            Log.Warn($"Hintergrund-Schreiben von {path} war fehlgeschlagen: {ex.GetBaseException().Message}");
        }
    }

    private static CaptureFiles filesFor(string pngOrJsonPath) {
        string fullPath = Path.GetFullPath(pngOrJsonPath);
        return fullPath.EndsWith(CaptureFiles.JsonExtension, StringComparison.OrdinalIgnoreCase) ? CaptureFiles.FromJson(fullPath) : CaptureFiles.FromPng(fullPath);
    }

    private static void deleteQuietly(string path) {
        try {
            File.Delete(path);
        } catch(Exception ex) {
            Log.Warn($"{path} konnte nicht entfernt werden: {ex.Message}");
        }
    }

    private static bool isTaken(string folder, string baseName) {
        CaptureFiles files = CaptureFiles.FromBase(folder, baseName);
        return File.Exists(files.PngPath) || File.Exists(files.JsonPath) || File.Exists(files.ThumbPath) || File.Exists(files.PngPath + ".tmp");
    }

    private static IEnumerable<string> getNumericFolders(string parent, int digits) {
        return Directory.EnumerateDirectories(parent)
            .Where(path => {
                string name = Path.GetFileName(path);
                return name.Length == digits && name.All(char.IsAsciiDigit);
            })
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal);
    }
}
