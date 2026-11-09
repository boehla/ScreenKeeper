using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
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
        DateTime time = document.CapturedAt.LocalDateTime;
        string folder = Path.Combine(RootFolder, time.ToString("yyyy", CultureInfo.InvariantCulture), time.ToString("MM", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
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
        List<FileInfo> pngs = new List<FileInfo>();
        foreach(FileInfo file in new DirectoryInfo(folder).EnumerateFiles()) {
            names.Add(file.Name);
            if(file.Name.EndsWith(CaptureFiles.PngExtension, StringComparison.OrdinalIgnoreCase)) pngs.Add(file);
        }
        List<CaptureEntry> entries = new List<CaptureEntry>(pngs.Count);
        foreach(FileInfo png in pngs) {
            string baseName = png.Name.Substring(0, png.Name.Length - CaptureFiles.PngExtension.Length);
            DateTime time = GetCaptureTime(baseName, pattern, png.LastWriteTime);
            bool hasJson = names.Contains(baseName + CaptureFiles.JsonExtension);
            bool hasThumb = names.Contains(baseName + CaptureFiles.ThumbExtension);
            entries.Add(new CaptureEntry(CaptureFiles.FromPng(png.FullName), time, png.Length, hasJson, hasThumb));
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
        return new CaptureEntry(files, time, png.Length, File.Exists(files.JsonPath), File.Exists(files.ThumbPath));
    }

    public OpenedCapture OpenOrCreate(string pngOrJsonPath) {
        string fullPath = Path.GetFullPath(pngOrJsonPath);
        CaptureFiles files = fullPath.EndsWith(CaptureFiles.JsonExtension, StringComparison.OrdinalIgnoreCase) ? CaptureFiles.FromJson(fullPath) : CaptureFiles.FromPng(fullPath);
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
