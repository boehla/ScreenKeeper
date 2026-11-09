using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.Tests;

public class CaptureFileNameTests {
    [Fact]
    public void ParsesDefaultPatternWithSuffix() {
        Assert.True(CaptureFileName.TryParseTime("2026-11-09_14-32-05", AppSettings.DefaultFileNamePattern, out DateTime time));
        Assert.Equal(new DateTime(2026, 11, 9, 14, 32, 5), time);
        Assert.True(CaptureFileName.TryParseTime("2026-11-09_14-32-05_3", AppSettings.DefaultFileNamePattern, out time));
        Assert.Equal(new DateTime(2026, 11, 9, 14, 32, 5), time);
        Assert.False(CaptureFileName.TryParseTime("Urlaub", AppSettings.DefaultFileNamePattern, out _));
        Assert.False(CaptureFileName.TryParseTime("2026-13-09_14-32-05", AppSettings.DefaultFileNamePattern, out _));
        Assert.False(CaptureFileName.TryParseTime("2026-02-30_14-32-05", AppSettings.DefaultFileNamePattern, out _));
    }

    [Fact]
    public void ParsesCustomPatternLikeFormatFileName() {
        string pattern = "Shot {dd}.{MM}.{yyyy} {HH}{mm}";
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 0);
        string name = StorageService.FormatFileName(pattern, time);
        Assert.True(CaptureFileName.TryParseTime(name, pattern, out DateTime parsed));
        Assert.Equal(time, parsed);
        // ungültige Zeichen werden wie beim Erzeugen zu "_"
        string odd = "a:b {yyyy}{MM}{dd}";
        string oddName = StorageService.FormatFileName(odd, time);
        Assert.True(CaptureFileName.TryParseTime(oddName, odd, out parsed));
        Assert.Equal(time.Date, parsed);
        // ohne Datum kein Ergebnis
        Assert.False(CaptureFileName.TryParseTime("14-32", "{HH}-{mm}", out _));
    }

    [Fact]
    public void Validate_PatternErrors() {
        Assert.Null(CaptureFileName.Validate(AppSettings.DefaultFileNamePattern));
        Assert.Null(CaptureFileName.Validate(""));
        Assert.Null(CaptureFileName.Validate("Bildschirm {yyyy}{MM}{dd} {HH}.{mm}.{ss}"));
        Assert.Contains("Ungültige Zeichen", CaptureFileName.Validate("a/b:c"));
        Assert.Contains("/", CaptureFileName.Validate("a/b"));
        Assert.Contains("{jahr}", CaptureFileName.Validate("{jahr}-{MM}"));
        Assert.Contains("{yyyy", CaptureFileName.Validate("x-{yyyy"));
        Assert.NotNull(CaptureFileName.Validate("a}b"));
        Assert.NotNull(CaptureFileName.Validate("..."));
    }
}

public class StorageHistoryTests {
    private static StorageService createStorage(TempFolder temp, out SettingsService settings) {
        settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
        settings.Load();
        settings.Settings.StorageFolder = Path.Combine(temp.Path, "ablage");
        return new StorageService(settings, new RenderService());
    }

    private static CaptureFiles touch(string folder, string baseName, bool json, bool thumb) {
        Directory.CreateDirectory(folder);
        CaptureFiles files = CaptureFiles.FromBase(folder, baseName);
        File.WriteAllBytes(files.PngPath, new byte[] { 1, 2, 3 });
        if(json) File.WriteAllText(files.JsonPath, "{}");
        if(thumb) File.WriteAllBytes(files.ThumbPath, new byte[] { 1 });
        return files;
    }

    [Fact]
    public void ReadFolder_DetectsMissingJsonAndThumb_AndSortsNewestFirst() {
        using (TempFolder temp = new TempFolder()) {
            string folder = Path.Combine(temp.Path, "2026", "11");
            touch(folder, "2026-11-09_10-00-00", true, true);
            touch(folder, "2026-11-09_12-00-00", false, true);
            touch(folder, "2026-11-09_12-00-00_2", true, false);
            CaptureFiles manual = touch(folder, "Mein Bild", false, false);
            DateTime manualTime = new DateTime(2026, 11, 9, 11, 0, 0);
            File.SetLastWriteTime(manual.PngPath, manualTime);
            File.WriteAllText(Path.Combine(folder, "notiz.txt"), "x");

            List<CaptureEntry> entries = StorageService.ReadFolder(folder, AppSettings.DefaultFileNamePattern);
            Assert.Equal(new[] { "2026-11-09_12-00-00_2", "2026-11-09_12-00-00", "Mein Bild", "2026-11-09_10-00-00" }, entries.Select(e => e.Name).ToArray());
            Assert.True(entries[0].HasJson);
            Assert.False(entries[0].HasThumbnail);
            Assert.False(entries[1].HasJson);
            Assert.True(entries[1].HasThumbnail);
            Assert.False(entries[2].HasJson);
            Assert.Equal(manualTime, entries[2].Time);
            Assert.Equal(3, entries[3].Size);
        }
    }

    [Fact]
    public void EnumerateCaptures_SkipsMonthsOutsideRange() {
        using (TempFolder temp = new TempFolder()) {
            StorageService storage = createStorage(temp, out _);
            string root = storage.RootFolder;
            touch(Path.Combine(root, "2026", "11"), "2026-11-02_10-00-00", true, true);
            touch(Path.Combine(root, "2026", "10"), "2026-10-15_10-00-00", true, true);
            touch(Path.Combine(root, "2026", "09"), "2026-09-01_10-00-00", true, true);
            touch(Path.Combine(root, "2025", "12"), "2025-12-24_10-00-00", true, true);
            Directory.CreateDirectory(Path.Combine(root, "Export"));
            touch(Path.Combine(root, "Export"), "export", false, false);

            Assert.Equal(4, storage.EnumerateCaptures().Count());
            Assert.Equal(new[] { "2026-11-02_10-00-00", "2026-10-15_10-00-00" }, storage.EnumerateCaptures(new DateTime(2026, 10, 1), null).Select(e => e.Name).ToArray());
            Assert.Equal(new[] { "2026-09-01_10-00-00", "2025-12-24_10-00-00" }, storage.EnumerateCaptures(null, new DateTime(2026, 9, 30)).Select(e => e.Name).ToArray());
            Assert.Equal(new[] { "2026-10-15_10-00-00" }, storage.EnumerateCaptures(new DateTime(2026, 10, 10), new DateTime(2026, 10, 20)).Select(e => e.Name).ToArray());
        }
    }

    [Fact]
    public void OpenOrCreate_CreatesValidDocumentForImageOnly() {
        using (TempFolder temp = new TempFolder()) {
            Sta.Run(() => {
                StorageService storage = createStorage(temp, out _);
                string folder = Path.Combine(storage.RootFolder, "2026", "11");
                Directory.CreateDirectory(folder);
                string png = Path.Combine(folder, "2026-11-07_08-09-10.png");
                ImageFiles.WritePng(TestImages.CreatePattern(640, 360), png);

                OpenedCapture opened = storage.OpenOrCreate(png);
                Assert.True(opened.Created);
                CaptureFiles files = CaptureFiles.FromPng(png);
                Assert.Equal(files.JsonPath, opened.JsonPath);
                Assert.True(File.Exists(files.JsonPath));
                Assert.True(File.Exists(files.ThumbPath));

                CaptureDocument document = storage.LoadDocument(files.JsonPath);
                Assert.Equal(CaptureDocument.CurrentFormatVersion, document.FormatVersion);
                Assert.Equal("2026-11-07_08-09-10.png", document.Image);
                Assert.Equal(new PixelRect(0, 0, 640, 360), document.VirtualScreen);
                MonitorInfo monitor = Assert.Single(document.Monitors);
                Assert.True(monitor.Primary);
                Assert.Equal(new PixelRect(0, 0, 640, 360), monitor.Bounds);
                Assert.Equal(1.0, monitor.Scale);
                Assert.Equal(new DateTime(2026, 11, 7, 8, 9, 10), document.CapturedAt.LocalDateTime);
                Assert.Null(document.Selection);
                Assert.False(document.IsEdited);
                Assert.Equal(png, storage.ResolveImagePath(document, files.JsonPath));

                // zweites Öffnen lädt das vorhandene Dokument
                document.Selection = new PixelRect(1, 2, 3, 4);
                storage.SaveDocument(document, files.JsonPath);
                OpenedCapture again = storage.OpenOrCreate(files.JsonPath);
                Assert.False(again.Created);
                Assert.Equal(new PixelRect(1, 2, 3, 4), again.Document.Selection);

                // defekte JSON wird gesichert und ersetzt
                File.WriteAllText(files.JsonPath, "{ kaputt");
                OpenedCapture repaired = storage.OpenOrCreate(png);
                Assert.True(repaired.Created);
                Assert.True(File.Exists(files.JsonPath + ".broken"));
                Assert.Null(repaired.Document.Selection);
            });
        }
    }

    [Fact]
    public void GetEntry_AndEnsureThumbnail() {
        using (TempFolder temp = new TempFolder()) {
            Sta.Run(() => {
                StorageService storage = createStorage(temp, out _);
                string folder = Path.Combine(storage.RootFolder, "2026", "11");
                Directory.CreateDirectory(folder);
                CaptureFiles files = CaptureFiles.FromBase(folder, "2026-11-09_01-02-03");
                Assert.Null(storage.GetEntry(files));
                ImageFiles.WritePng(TestImages.CreatePattern(1000, 500), files.PngPath);
                CaptureEntry entry = storage.GetEntry(files)!;
                Assert.Equal(new DateTime(2026, 11, 9, 1, 2, 3), entry.Time);
                Assert.False(entry.HasJson);
                Assert.False(entry.HasThumbnail);
                Assert.True(storage.EnsureThumbnail(files));
                BitmapSource thumb = ImageFiles.LoadBgra32(files.ThumbPath);
                Assert.Equal(StorageService.ThumbnailMaxWidth, thumb.PixelWidth);
            });
        }
    }
}

public class RetentionServiceTests {
    [Fact]
    public void FindExpired_OlderThanDays() {
        DateTime now = new DateTime(2026, 11, 9, 12, 0, 0);
        List<CaptureEntry> entries = new List<CaptureEntry> {
            HistoryGroupingTests.entry(now.AddDays(-1)),
            HistoryGroupingTests.entry(now.AddDays(-30).AddMinutes(1)),
            HistoryGroupingTests.entry(now.AddDays(-30).AddMinutes(-1)),
            HistoryGroupingTests.entry(now.AddDays(-400))
        };
        Assert.Equal(2, RetentionService.FindExpired(entries, now, 30).Count);
        Assert.Empty(RetentionService.FindExpired(entries, now, 0));
        Assert.Single(RetentionService.FindExpired(entries, now, 365));
        Assert.Equal(3, RetentionService.FindExpired(entries, now, 1).Count);
    }

    [Fact]
    public void DryRun_FindsButKeepsFiles() {
        using (TempFolder temp = new TempFolder()) {
            SettingsService settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
            settings.Load();
            settings.Settings.StorageFolder = Path.Combine(temp.Path, "ablage");
            StorageService storage = new StorageService(settings, new RenderService());
            DateTime now = new DateTime(2026, 11, 9, 12, 0, 0);
            List<CaptureFiles> all = new List<CaptureFiles>();
            foreach(int daysAgo in new[] { 0, 5, 9, 11, 40, 100 }) {
                DateTime time = now.AddDays(-daysAgo);
                string folder = Path.Combine(storage.RootFolder, time.ToString("yyyy"), time.ToString("MM"));
                Directory.CreateDirectory(folder);
                CaptureFiles files = CaptureFiles.FromBase(folder, StorageService.FormatFileName(AppSettings.DefaultFileNamePattern, time));
                File.WriteAllBytes(files.PngPath, new byte[] { 1 });
                File.WriteAllText(files.JsonPath, "{}");
                File.WriteAllBytes(files.ThumbPath, new byte[] { 1 });
                all.Add(files);
            }
            RetentionService retention = new RetentionService(storage, settings);
            RetentionResult result = retention.Run(now, 10, true);
            Assert.True(result.DryRun);
            Assert.Equal(3, result.Expired.Count);
            Assert.All(result.Expired, e => Assert.True(e.Time < now.AddDays(-10)));
            Assert.All(all, f => Assert.True(File.Exists(f.PngPath) && File.Exists(f.JsonPath) && File.Exists(f.ThumbPath)));
            Assert.Empty(retention.Run(now, 0, true).Expired);
        }
    }
}
