using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

/// <summary>
/// 0.7.0: Kopie einer Aufnahme, Markierung „permanent“ und Aufräumen, Programm im Vordergrund, Update-Prüfung.
/// </summary>
public class DuplicateCaptureTests {
    private static StorageService createStorage(TempFolder temp) {
        SettingsService settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
        settings.Load();
        settings.Settings.StorageFolder = Path.Combine(temp.Path, "ablage");
        return new StorageService(settings, new RenderService());
    }

    /// <summary>Aufnahme mit PNG, Vorschaubild und JSON (zwei Zeichnungen, ein unbekannter Typ, permanent).</summary>
    private static CaptureFiles createCapture(StorageService storage, string baseName) {
        string folder = Path.Combine(storage.RootFolder, "2026", "11");
        Directory.CreateDirectory(folder);
        CaptureFiles files = CaptureFiles.FromBase(folder, baseName);
        BitmapSource image = TestImages.CreatePattern(64, 40);
        ImageFiles.WritePng(image, files.PngPath);
        StorageService.WriteThumbnail(image, files.ThumbPath);
        string json = $$"""
            {
              "formatVersion": 1,
              "capturedAt": "2026-11-09T14:32:05+01:00",
              "image": "{{baseName}}.png",
              "virtualScreen": { "x": 0, "y": 0, "width": 64, "height": 40 },
              "monitors": [ { "device": "D", "bounds": { "x": 0, "y": 0, "width": 64, "height": 40 }, "primary": true, "scale": 1.0 } ],
              "windows": [],
              "foreground": { "title": "Notizen – Editor", "process": "notepad" },
              "permanent": true,
              "selection": { "x": 2, "y": 3, "width": 20, "height": 10 },
              "annotations": [
                { "id": "a1", "type": "rectangle", "rect": { "x": 1, "y": 2, "width": 3, "height": 4 }, "stroke": "#FF000000", "thickness": 2 },
                { "id": "a2", "type": "sticker", "emoji": "x" },
                { "id": "a3", "type": "number", "x": 10, "y": 10, "number": 1, "color": "#FFE53935", "size": 32 }
              ],
              "ocrText": null
            }
            """;
        File.WriteAllText(files.JsonPath, json);
        return files;
    }

    private static string[] hashes(CaptureFiles files) {
        return new[] { files.PngPath, files.JsonPath, files.ThumbPath }.Select(path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
    }

    [Fact]
    public void Duplicate_CopiesThreeFiles_WithNewIds_AndLeavesOriginalUnchanged() {
        using (TempFolder temp = new TempFolder()) {
            Sta.Run(() => {
                StorageService storage = createStorage(temp);
                CaptureFiles original = createCapture(storage, "2026-11-09_14-32-05");
                string[] before = hashes(original);

                CaptureFiles copy = storage.DuplicateCapture(original.JsonPath);

                Assert.Equal("2026-11-09_14-32-05_2", CaptureFiles.GetBaseName(copy.PngPath));
                Assert.True(File.Exists(copy.PngPath) && File.Exists(copy.JsonPath) && File.Exists(copy.ThumbPath));
                Assert.Equal(File.ReadAllBytes(original.PngPath), File.ReadAllBytes(copy.PngPath));
                Assert.Equal(before, hashes(original));

                CaptureDocument source = storage.LoadDocument(original.JsonPath);
                CaptureDocument copied = storage.LoadDocument(copy.JsonPath);
                Assert.Equal("2026-11-09_14-32-05_2.png", copied.Image);
                Assert.False(copied.Permanent);
                Assert.True(source.Permanent);
                Assert.Equal(source.Selection, copied.Selection);
                Assert.Equal("notepad", copied.Foreground!.Process);
                Assert.Equal(3, copied.Annotations.Count);
                string[] oldIds = source.Annotations.Select(a => a.Id).ToArray();
                string[] newIds = copied.Annotations.Select(a => a.Id).ToArray();
                Assert.Empty(oldIds.Intersect(newIds));
                Assert.Equal(3, newIds.Distinct().Count());
                Assert.All(newIds, id => Assert.Equal(32, id.Length));
                // unbekannter Typ: neue Id in den Rohdaten, sonst unverändert
                UnknownAnnotation sticker = Assert.IsType<UnknownAnnotation>(copied.Annotations[1]);
                Assert.Equal(sticker.Id, sticker.Raw.GetProperty("id").GetString());
                Assert.Equal("x", sticker.Raw.GetProperty("emoji").GetString());
                Assert.IsType<NumberStampAnnotation>(copied.Annotations[2]);

                // weitere Kopien: _3, auch von der Kopie aus (kein _2_2)
                CaptureFiles second = storage.DuplicateCapture(original.PngPath);
                CaptureFiles third = storage.DuplicateCapture(copy.JsonPath);
                Assert.Equal("2026-11-09_14-32-05_3", CaptureFiles.GetBaseName(second.PngPath));
                Assert.Equal("2026-11-09_14-32-05_4", CaptureFiles.GetBaseName(third.PngPath));
            });
        }
    }

    [Fact]
    public void Duplicate_OfImageWithoutJson_CreatesJsonOnlyForTheCopy() {
        using (TempFolder temp = new TempFolder()) {
            Sta.Run(() => {
                StorageService storage = createStorage(temp);
                string folder = Path.Combine(storage.RootFolder, "2026", "11");
                Directory.CreateDirectory(folder);
                CaptureFiles files = CaptureFiles.FromBase(folder, "Mein Bild");
                ImageFiles.WritePng(TestImages.CreatePattern(30, 20), files.PngPath);

                CaptureFiles copy = storage.DuplicateCapture(files.PngPath);

                Assert.False(File.Exists(files.JsonPath));
                Assert.Equal("Mein Bild_2", CaptureFiles.GetBaseName(copy.PngPath));
                Assert.True(File.Exists(copy.JsonPath));
                Assert.True(File.Exists(copy.ThumbPath));
                Assert.Equal(30, storage.LoadDocument(copy.JsonPath).VirtualScreen.Width);
            });
        }
    }

    [Fact]
    public void CopyStem_RemovesOnlyCollisionSuffixOfCaptureNames() {
        string pattern = AppSettings.DefaultFileNamePattern;
        Assert.Equal("2026-11-09_14-32-05", StorageService.GetCopyStem("2026-11-09_14-32-05", pattern));
        Assert.Equal("2026-11-09_14-32-05", StorageService.GetCopyStem("2026-11-09_14-32-05_2", pattern));
        Assert.Equal("2026-11-09_14-32-05", StorageService.GetCopyStem("2026-11-09_14-32-05_17", pattern));
        Assert.Equal("Bericht_2", StorageService.GetCopyStem("Bericht_2", pattern));
        Assert.Equal("Bericht", StorageService.GetCopyStem("Bericht", pattern));
    }

    [Fact]
    public async Task History_DuplicateAndPermanent_UpdateListAndJson() {
        using (TempFolder temp = new TempFolder()) {
            StorageService storage = createStorage(temp);
            Sta.Run(() => {
                createCapture(storage, "2026-11-09_14-32-05");
                createCapture(storage, "2026-11-09_15-00-00");
            });
            HistoryViewModel viewModel = new HistoryViewModel(storage, null, () => new DateTime(2026, 11, 9, 18, 0, 0));
            await viewModel.RefreshAsync();
            HistoryItem original = viewModel.Items.Single(i => i.Name == "2026-11-09_14-32-05");

            HistoryItem? copy = await viewModel.DuplicateAsync(original);
            Assert.NotNull(copy);
            Assert.Equal(3, viewModel.ItemCount);
            Assert.Equal(viewModel.Rows.IndexOf(original) - 1, viewModel.Rows.IndexOf(copy));
            Assert.Equal(3, ((HistoryHeaderRow)viewModel.Rows[0]).Count);

            // Kopie ist nicht permanent → umschalten markiert sie
            bool? value = await viewModel.TogglePermanentAsync(copy);
            Assert.True(value);
            Assert.True(copy!.IsPermanent);
            Assert.True(storage.LoadDocument(copy.Files.JsonPath).Permanent);
            Assert.Contains("\"permanent\": true", File.ReadAllText(copy.Files.JsonPath));
            value = await viewModel.TogglePermanentAsync(copy);
            Assert.False(value);
            Assert.DoesNotContain("\"permanent\"", File.ReadAllText(copy.Files.JsonPath));

            // Filter „Permanent“: nur die beiden Originale
            viewModel.FilterPermanent = true;
            await viewModel.LoadAllAsync();
            Assert.Equal(2, viewModel.ItemCount);
            Assert.All(viewModel.Items, i => Assert.True(i.IsPermanent));
        }
    }
}

public class PermanentAndRetentionTests {
    [Fact]
    public void Permanent_RoundtripAndOldFilesWithoutField() {
        CaptureDocument document = new CaptureDocument { Image = "a.png" };
        Assert.DoesNotContain("permanent", CaptureDocumentSerializer.Serialize(document));
        document.Permanent = true;
        string json = CaptureDocumentSerializer.Serialize(document);
        Assert.Contains("\"permanent\": true", json);
        Assert.True(CaptureDocumentSerializer.Deserialize(json).Permanent);
        Assert.False(CaptureDocumentSerializer.Deserialize("""{ "formatVersion": 1, "image": "a.png" }""").Permanent);
    }

    [Fact]
    public void SetPermanent_CreatesMissingJson() {
        using (TempFolder temp = new TempFolder()) {
            Sta.Run(() => {
                SettingsService settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
                settings.Load();
                StorageService storage = new StorageService(settings, new RenderService()) { RootFolderOverride = temp.Path };
                CaptureFiles files = CaptureFiles.FromBase(temp.Path, "2026-11-09_10-00-00");
                ImageFiles.WritePng(TestImages.CreatePattern(10, 10), files.PngPath);
                storage.SetPermanent(files.PngPath, true);
                Assert.True(storage.LoadDocument(files.JsonPath).Permanent);
                CaptureEntry entry = storage.GetEntry(files)!;
                Assert.True(storage.GetMetadata(entry).Permanent);
                storage.SetPermanent(files.JsonPath, false);
                Assert.False(storage.GetMetadata(storage.GetEntry(files)!).Permanent);
            });
        }
    }

    [Fact]
    public void Default_Is30Days_ExplicitZeroStaysOff() {
        Assert.Equal(30, new AppSettings().RetentionDays);
        using (TempFolder temp = new TempFolder()) {
            string path = Path.Combine(temp.Path, "settings.json");
            File.WriteAllText(path, """{ "magnifierZoom": 8 }""");
            SettingsService service = new SettingsService(path);
            service.Load();
            Assert.Equal(30, service.Settings.RetentionDays);
            // bewusst abgeschaltet (Datei ab 0.7.0 mit settingsVersion) bleibt aus; eine alte Datei ohne Versionsfeld
            // mit 0 wird dagegen auf den neuen Standard umgestellt (siehe SettingsServiceTests)
            File.WriteAllText(path, """{ "settingsVersion": 1, "retentionDays": 0 }""");
            service.Load();
            Assert.Equal(0, service.Settings.RetentionDays);
        }
    }

    [Fact]
    public void Run_SkipsPermanentAndUnreadable_ImageOnlyCountsAsNotPermanent() {
        using (TempFolder temp = new TempFolder()) {
            SettingsService settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
            settings.Load();
            settings.Settings.StorageFolder = Path.Combine(temp.Path, "ablage");
            StorageService storage = new StorageService(settings, new RenderService());
            DateTime now = new DateTime(2026, 11, 9, 12, 0, 0);
            CaptureFiles add(int daysAgo, string? json) {
                DateTime time = now.AddDays(-daysAgo);
                string folder = Path.Combine(storage.RootFolder, time.ToString("yyyy"), time.ToString("MM"));
                Directory.CreateDirectory(folder);
                CaptureFiles files = CaptureFiles.FromBase(folder, StorageService.FormatFileName(AppSettings.DefaultFileNamePattern, time));
                File.WriteAllBytes(files.PngPath, new byte[] { 1 });
                if(json != null) File.WriteAllText(files.JsonPath, json);
                return files;
            }
            add(1, """{ "permanent": true }""");
            CaptureFiles permanent = add(40, """{ "permanent": true }""");
            CaptureFiles normal = add(41, "{}");
            CaptureFiles imageOnly = add(42, null);
            CaptureFiles broken = add(43, "{ kaputt");

            RetentionService retention = new RetentionService(storage, settings);
            RetentionResult result = retention.Run(now, AppSettings.DefaultRetentionDays, true);
            Assert.Equal(new[] { normal.PngPath, imageOnly.PngPath }, result.Expired.Select(e => e.Files.PngPath).ToArray());
            Assert.Equal(2, result.Kept);
            Assert.DoesNotContain(result.Expired, e => e.Files.PngPath == permanent.PngPath || e.Files.PngPath == broken.PngPath);
            Assert.True(RetentionService.IsKept(new CaptureMetadata(false, false, true, "", "")));
            Assert.False(RetentionService.IsKept(CaptureMetadata.None));
        }
    }
}

public class ForegroundTests {
    [Fact]
    public void Foreground_IsSerialized_AndWindowFlagOnlyWhenTrue() {
        CaptureDocument document = new CaptureDocument {
            Image = "a.png",
            Foreground = new ForegroundInfo { Title = "Neuer Tab – Chrome", Process = "chrome" },
            Windows = new List<WindowInfo> {
                new WindowInfo { Title = "Neuer Tab – Chrome", Process = "chrome", Bounds = new PixelRect(0, 0, 10, 10), Z = 0, Foreground = true },
                new WindowInfo { Title = "Explorer", Process = "explorer", Bounds = new PixelRect(0, 0, 10, 10), Z = 1 }
            }
        };
        string json = CaptureDocumentSerializer.Serialize(document);
        using (JsonDocument parsed = JsonDocument.Parse(json)) {
            JsonElement foreground = parsed.RootElement.GetProperty("foreground");
            Assert.Equal("chrome", foreground.GetProperty("process").GetString());
            Assert.Equal("Neuer Tab – Chrome", foreground.GetProperty("title").GetString());
            JsonElement windows = parsed.RootElement.GetProperty("windows");
            Assert.True(windows[0].GetProperty("foreground").GetBoolean());
            Assert.False(windows[1].TryGetProperty("foreground", out _));
        }
        Assert.Contains("Neuer Tab – Chrome", json);
        CaptureDocument loaded = CaptureDocumentSerializer.Deserialize(json);
        Assert.Equal("chrome", loaded.Foreground!.Process);
        Assert.True(loaded.Windows[0].Foreground);
        Assert.False(loaded.Windows[1].Foreground);
        // ältere Dateien: kein Vordergrundprogramm
        Assert.Null(CaptureDocumentSerializer.Deserialize("""{ "image": "a.png" }""").Foreground);
        ForegroundInfo? nullTitle = CaptureDocumentSerializer.Deserialize("""{ "image": "a.png", "foreground": { "title": null } }""").Foreground;
        Assert.Equal("", nullTitle!.Title);
        Assert.Equal("", nullTitle.Process);
    }

    [Fact]
    public void Metadata_FromDocument() {
        CaptureDocument document = new CaptureDocument { Permanent = true, Foreground = new ForegroundInfo { Process = "code", Title = "x.cs" } };
        CaptureMetadata metadata = CaptureMetadata.FromDocument(document);
        Assert.True(metadata.Permanent);
        Assert.False(metadata.IsEdited);
        Assert.Equal("code", metadata.ForegroundProcess);
        Assert.True(metadata.HasForeground);
        Assert.False(CaptureMetadata.None.HasForeground);
    }

    [Fact]
    public void ForegroundText_ShortensTitle() {
        Assert.Equal("chrome · Neuer Tab", HistoryItem.FormatForeground("chrome", "Neuer Tab", 60));
        Assert.Equal("chrome", HistoryItem.FormatForeground("chrome", "", 60));
        Assert.Equal("Titel", HistoryItem.FormatForeground("", "Titel", 60));
        string shortened = HistoryItem.FormatForeground("p", new string('a', 100), 10);
        Assert.Equal("p · aaaaaaaaa…", shortened);
    }

    [Fact]
    public void TextFilter_SearchesProcessAndTitle() {
        DateTime now = new DateTime(2026, 11, 9, 15, 0, 0);
        CaptureEntry chrome = HistoryGroupingTests.entry(now, "2026-11-09_15-00-00");
        chrome.Metadata = new CaptureMetadata(false, false, false, "chrome", "Ticket 4711 – Jira");
        CaptureEntry unknown = HistoryGroupingTests.entry(now, "2026-11-09_15-00-01");
        Assert.True(new HistoryFilter(null, null, "CHROME").Matches(chrome));
        Assert.True(new HistoryFilter(null, null, "jira").Matches(chrome));
        Assert.False(new HistoryFilter(null, null, "excel").Matches(chrome));
        Assert.False(new HistoryFilter(null, null, "chrome").Matches(unknown));
        Assert.True(new HistoryFilter(null, null, "15-00-01").Matches(unknown));
        Assert.True(new HistoryFilter(null, null, "chrome").NeedsMetadata);
        Assert.False(HistoryFilter.All.NeedsMetadata);
        Assert.False(new HistoryFilter(null, null, null, true).Matches(chrome));
        chrome.Metadata = chrome.Metadata with { Permanent = true };
        Assert.True(new HistoryFilter(null, null, null, true).Matches(chrome));
        Assert.False(new HistoryFilter(null, null, null, true).IsEmpty);
    }

    [Fact]
    public void Pager_LoadsMetadataOncePerEntry_OnlyWhenNeeded() {
        DateTime start = new DateTime(2026, 11, 9, 12, 0, 0);
        List<CaptureEntry> entries = Enumerable.Range(0, 100).Select(i => HistoryGroupingTests.entry(start.AddMinutes(-30 * i))).ToList();
        int loads = 0;
        CaptureMetadata load(CaptureEntry entry) {
            loads++;
            int index = entries.IndexOf(entry);
            entry.Metadata = new CaptureMetadata(false, false, false, index % 10 == 0 ? "chrome" : "notepad", "");
            return entry.Metadata;
        }
        HistoryPager pager = new HistoryPager(entries, new HistoryFilter(null, null, "chrome"), load);
        List<CaptureEntry> found = new List<CaptureEntry>();
        while(!pager.IsComplete) found.AddRange(pager.NextBlock(5));
        Assert.Equal(10, found.Count);
        Assert.Equal(100, loads);
        // zweiter Durchlauf: Kennzeichen sind schon geladen
        HistoryPager again = new HistoryPager(entries, new HistoryFilter(null, null, "notepad"), load);
        Assert.Equal(90, again.NextBlock(1000).Count);
        Assert.Equal(100, loads);
        // ohne Text kein Lesen; Treffer im Dateinamen braucht keine JSON
        HistoryPager plain = new HistoryPager(entries.Select(e => new CaptureEntry(e.Files, e.Time)), HistoryFilter.All, load);
        plain.NextBlock(1000);
        HistoryPager byName = new HistoryPager(entries.Select(e => new CaptureEntry(e.Files, e.Time)), new HistoryFilter(null, null, "2026-11"), load);
        Assert.Equal(100, byName.NextBlock(1000).Count);
        Assert.Equal(100, loads);
    }
}

public class UpdateServiceTests {
    private static string releaseJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "github-release-latest.json"));

    [Fact]
    public void ParseVersion_AndCompare() {
        Assert.Equal(new Version(0, 7, 0, 0), UpdateService.ParseVersion("v0.7.0"));
        Assert.Equal(new Version(0, 7, 0, 0), UpdateService.ParseVersion("0.7"));
        Assert.Equal(new Version(1, 0, 0, 0), UpdateService.ParseVersion("V1"));
        Assert.Equal(new Version(0, 8, 0, 0), UpdateService.ParseVersion("0.8.0-beta.1"));
        Assert.Equal(new Version(0, 7, 0, 0), UpdateService.ParseVersion("0.7.0+abcdef"));
        Assert.Null(UpdateService.ParseVersion("latest"));
        Assert.Null(UpdateService.ParseVersion(""));
        Assert.True(UpdateService.IsNewer("v0.8.0", "0.7.0"));
        Assert.True(UpdateService.IsNewer("v0.10.0", "0.9.5"));
        Assert.True(UpdateService.IsNewer("v0.7.1", "0.7"));
        Assert.False(UpdateService.IsNewer("v0.7.0", "0.7"));
        Assert.False(UpdateService.IsNewer("v0.6.1", "0.7.0"));
        Assert.False(UpdateService.IsNewer("kaputt", "0.7.0"));
    }

    [Fact]
    public void SelectAsset_FromGitHubResponse() {
        string json = releaseJson;
        UpdateInfo selfContained = UpdateService.SelectAsset(json, true, true);
        Assert.Equal("0.6.1", selfContained.Version);
        Assert.Equal("https://github.com/boehla/ScreenKeeper/releases/tag/v0.6.1", selfContained.ReleaseUrl);
        Assert.Equal("ScreenKeeper-0.6.1-win-x64.zip", selfContained.AssetName);
        Assert.Equal("https://github.com/boehla/ScreenKeeper/releases/download/v0.6.1/ScreenKeeper-0.6.1-win-x64.zip", selfContained.AssetUrl);

        UpdateInfo frameworkDependent = UpdateService.SelectAsset(json, true, false);
        Assert.Equal("ScreenKeeper-0.6.1-win-x64-framework-dependent.zip", frameworkDependent.AssetName);
        Assert.EndsWith("/ScreenKeeper-0.6.1-win-x64-framework-dependent.zip", frameworkDependent.AssetUrl);

        // Entwicklungs-Build: kein Paket, nur die Release-Seite
        UpdateInfo development = UpdateService.SelectAsset(json, false, false);
        Assert.Null(development.AssetName);
        Assert.Null(development.AssetUrl);
        Assert.Equal(selfContained.ReleaseUrl, development.ReleaseUrl);

        // passendes Paket fehlt
        UpdateInfo missing = UpdateService.SelectAsset("""{ "tag_name": "v0.9.0", "html_url": "https://github.com/boehla/ScreenKeeper/releases/tag/v0.9.0", "assets": [] }""", true, true);
        Assert.Null(missing.AssetUrl);
        Assert.Throws<InvalidDataException>(() => UpdateService.SelectAsset("""{ "name": "ohne Tag" }""", true, true));
        // nur https
        UpdateInfo insecure = UpdateService.SelectAsset("""{ "tag_name": "v0.9.0", "html_url": "http://x", "assets": [ { "name": "ScreenKeeper-0.9.0-win-x64.zip", "browser_download_url": "http://x/a.zip" } ] }""", true, true);
        Assert.Null(insecure.AssetUrl);
        Assert.Equal(UpdateService.ReleasesPageUrl, insecure.ReleaseUrl);
    }

    [Fact]
    public void Evaluate_NoUpdateWhenInstalledIsNewerOrEqual() {
        string json = releaseJson;
        Assert.False(UpdateService.Evaluate(json, "0.7.0", true, true).IsNewer);
        Assert.False(UpdateService.Evaluate(json, "0.6.1", true, true).IsNewer);
        UpdateCheckResult older = UpdateService.Evaluate(json, "0.6.0", true, false);
        Assert.True(older.IsNewer);
        Assert.Equal("0.6.1", older.Latest.Version);
        Assert.Equal("0.6.0", older.CurrentVersion);
    }

    [Fact]
    public void StoredState_ShowsUpdateWithoutNetwork() {
        using (TempFolder temp = new TempFolder()) {
            SettingsService settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
            settings.Load();
            string state = Path.Combine(temp.Path, "update.json");
            File.WriteAllText(state, """
                { "lastCheck": "2026-11-09T10:00:00.0000000+01:00",
                  "release": { "tag_name": "v0.9.0", "html_url": "https://github.com/boehla/ScreenKeeper/releases/tag/v0.9.0", "assets": [] } }
                """);
            UpdateService newer = new UpdateService(settings, state, "0.7.0");
            Assert.Equal("0.9.0", newer.Available!.Version);
            Assert.Equal(new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.FromHours(1)), newer.LastCheck);
            UpdateService current = new UpdateService(settings, state, "0.9.0");
            Assert.Null(current.Available);
            UpdateService missing = new UpdateService(settings, Path.Combine(temp.Path, "fehlt.json"), "0.7.0");
            Assert.Null(missing.Available);
            Assert.Null(missing.LastCheck);
        }
    }

    [Fact]
    public void ExtractUpdate_FindsExe_AndRejectsPackageWithout() {
        using (TempFolder temp = new TempFolder()) {
            string zip = Path.Combine(temp.Path, "update.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
                using (StreamWriter writer = new StreamWriter(archive.CreateEntry("ScreenKeeper.exe").Open())) {
                    writer.Write("MZ");
                }
                using (StreamWriter writer = new StreamWriter(archive.CreateEntry("README.md").Open())) {
                    writer.Write("x");
                }
            }
            string target = Path.Combine(temp.Path, "files");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "alt.txt"), "alt");
            string exe = UpdateService.ExtractUpdate(zip, target);
            Assert.Equal(Path.Combine(target, "ScreenKeeper.exe"), exe);
            Assert.False(File.Exists(Path.Combine(target, "alt.txt")));

            string empty = Path.Combine(temp.Path, "leer.zip");
            using (ZipArchive archive = ZipFile.Open(empty, ZipArchiveMode.Create)) {
                archive.CreateEntry("README.md");
            }
            Assert.Throws<InvalidDataException>(() => UpdateService.ExtractUpdate(empty, Path.Combine(temp.Path, "files2")));
        }
    }

    [Fact]
    public void InstallScript_QuotesPathsAndWaitsForProcess() {
        string script = UpdateService.BuildInstallScript(4242, @"C:\Temp\neu\ScreenKeeper.exe", @"C:\Programme\O'Brien\ScreenKeeper.exe", @"C:\log.txt");
        Assert.Contains("$processId = 4242", script);
        Assert.Contains("Wait-Process -Id $processId", script);
        Assert.Contains("$target = 'C:\\Programme\\O''Brien\\ScreenKeeper.exe'", script);
        Assert.Contains("Copy-Item -LiteralPath $source -Destination $target -Force", script);
        Assert.Contains("AddSeconds(30)", script);
        Assert.Contains("Start-Process -FilePath $target", script);
        Assert.Contains("Remove-Item -LiteralPath $PSCommandPath", script);
    }

    [Fact]
    public void AssetNames_MatchReleaseWorkflow() {
        Assert.Equal("ScreenKeeper-0.8.0-win-x64.zip", UpdateService.GetAssetName("0.8.0", true));
        Assert.Equal("ScreenKeeper-0.8.0-win-x64-framework-dependent.zip", UpdateService.GetAssetName("0.8.0", false));
        // Testlauf (testhost) ist kein Single-File-Build
        Assert.False(UpdateService.IsSingleFile);
    }
}
