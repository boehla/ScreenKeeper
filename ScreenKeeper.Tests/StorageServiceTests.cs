using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.Tests;

public class StorageServiceTests {
    [Fact]
    public void FormatFileName_ReplacesAllPlaceholders() {
        DateTime time = new DateTime(2026, 11, 9, 14, 32, 5);
        Assert.Equal("2026-11-09_14-32-05", StorageService.FormatFileName(AppSettings.DefaultFileNamePattern, time));
        Assert.Equal("Shot 09.11.2026 143205", StorageService.FormatFileName("Shot {dd}.{MM}.{yyyy} {HH}{mm}{ss}", time));
    }

    [Fact]
    public void FormatFileName_RemovesInvalidCharactersAndFallsBack() {
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5);
        Assert.Equal("a_b_c_2026", StorageService.FormatFileName("a/b:c?{yyyy}", time));
        Assert.Equal("2026-01-02_03-04-05", StorageService.FormatFileName("", time));
    }

    [Fact]
    public void GetUniqueBaseName_AppendsCollisionSuffix() {
        using (TempFolder temp = new TempFolder()) {
            Assert.Equal("shot", StorageService.GetUniqueBaseName(temp.Path, "shot"));
            File.WriteAllText(Path.Combine(temp.Path, "shot.png"), "");
            Assert.Equal("shot_2", StorageService.GetUniqueBaseName(temp.Path, "shot"));
            File.WriteAllText(Path.Combine(temp.Path, "shot_2.json"), "");
            Assert.Equal("shot_3", StorageService.GetUniqueBaseName(temp.Path, "shot"));
            File.WriteAllText(Path.Combine(temp.Path, "shot_3.thumb.jpg"), "");
            Assert.Equal("shot_4", StorageService.GetUniqueBaseName(temp.Path, "shot"));
        }
    }

    [Fact]
    public void CaptureFiles_DerivesPaths() {
        CaptureFiles files = CaptureFiles.FromJson(@"C:\x\2026\11\a.json");
        Assert.Equal(@"C:\x\2026\11\a.png", files.PngPath);
        Assert.Equal(@"C:\x\2026\11\a.thumb.jpg", files.ThumbPath);
        Assert.Equal(files, CaptureFiles.FromPng(@"C:\x\2026\11\a.png"));
    }

    [Fact]
    public void Thumbnail_IsAtMost320PixelsWide() {
        Sta.Run(() => {
            BitmapSource wide = TestImages.CreatePattern(1000, 500);
            BitmapSource thumb = StorageService.CreateThumbnail(wide);
            Assert.True(thumb.PixelWidth <= StorageService.ThumbnailMaxWidth);
            Assert.Equal(320, thumb.PixelWidth);
            Assert.InRange(thumb.PixelHeight, 159, 161);

            BitmapSource odd = StorageService.CreateThumbnail(TestImages.CreatePattern(3841, 1081));
            Assert.True(odd.PixelWidth <= StorageService.ThumbnailMaxWidth);

            BitmapSource small = StorageService.CreateThumbnail(TestImages.CreatePattern(200, 100));
            Assert.Equal(200, small.PixelWidth);
        });
    }

    [Fact]
    public void SaveNewCapture_WritesPngJsonAndThumbInYearMonthFolder() {
        using (TempFolder temp = new TempFolder()) {
            Sta.Run(() => {
                SettingsService settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
                settings.Load();
                settings.Settings.StorageFolder = Path.Combine(temp.Path, "ablage");
                StorageService storage = new StorageService(settings, new RenderService());
                BitmapSource original = TestImages.CreatePattern(1000, 400);
                CaptureDocument doc = new CaptureDocument {
                    CapturedAt = new DateTimeOffset(2026, 11, 9, 14, 32, 5, TimeSpan.FromHours(1)),
                    VirtualScreen = new PixelRect(0, 0, 1000, 400)
                };

                SavedCapture first = storage.SaveNewCapture(doc, original);
                Task.WaitAll(first.PngTask, first.ThumbnailTask);
                string expectedFolder = Path.Combine(temp.Path, "ablage", doc.CapturedAt.LocalDateTime.ToString("yyyy"), doc.CapturedAt.LocalDateTime.ToString("MM"));
                Assert.Equal(expectedFolder, Path.GetDirectoryName(first.Files.JsonPath));
                Assert.True(File.Exists(first.Files.PngPath));
                Assert.True(File.Exists(first.Files.JsonPath));
                Assert.True(File.Exists(first.Files.ThumbPath));
                Assert.Equal(Path.GetFileName(first.Files.PngPath), doc.Image);

                BitmapSource thumb = ImageFiles.LoadBgra32(first.Files.ThumbPath);
                Assert.True(thumb.PixelWidth <= 320);
                BitmapSource png = storage.LoadOriginal(first.Files.PngPath);
                Assert.Equal(TestImages.GetPixels(original), TestImages.GetPixels(png));

                // Zweite Aufnahme in derselben Sekunde → Suffix _2
                CaptureDocument second = new CaptureDocument { CapturedAt = doc.CapturedAt, VirtualScreen = doc.VirtualScreen };
                SavedCapture secondSaved = storage.SaveNewCapture(second, original);
                Task.WaitAll(secondSaved.PngTask, secondSaved.ThumbnailTask);
                Assert.EndsWith("_2.json", secondSaved.Files.JsonPath);

                List<CaptureEntry> entries = storage.EnumerateCaptures().ToList();
                Assert.Equal(2, entries.Count);

                storage.UpdateThumbnail(doc, original, first.Files.JsonPath);
                Assert.True(File.Exists(first.Files.ThumbPath));
            });
        }
    }
}
