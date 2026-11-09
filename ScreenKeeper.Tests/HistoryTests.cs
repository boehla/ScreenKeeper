using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

public class HistoryGroupingTests {
    private static readonly DateTime today = new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Local);

    [Fact]
    public void Label_TodayYesterdayAndLongDate() {
        Assert.Equal("Heute", HistoryGrouping.GetLabel(new DateTime(2026, 11, 9), today));
        Assert.Equal("Gestern", HistoryGrouping.GetLabel(new DateTime(2026, 11, 8, 23, 59, 59), today));
        Assert.Equal("Samstag, 7. November 2026", HistoryGrouping.GetLabel(new DateTime(2026, 11, 7), today));
        Assert.Equal("Donnerstag, 31. Dezember 2026", HistoryGrouping.GetLabel(new DateTime(2026, 12, 31), today));
    }

    [Fact]
    public void Label_YesterdayAcrossMonthAndYear() {
        DateTime newYear = new DateTime(2027, 1, 1, 0, 0, 1);
        Assert.Equal("Heute", HistoryGrouping.GetLabel(new DateTime(2027, 1, 1), newYear));
        Assert.Equal("Gestern", HistoryGrouping.GetLabel(new DateTime(2026, 12, 31, 23, 59, 59), newYear));
    }

    [Fact]
    public void Day_BoundaryAtMidnight() {
        Assert.Equal(new DateTime(2026, 11, 8), HistoryGrouping.GetDay(new DateTime(2026, 11, 8, 23, 59, 59)));
        Assert.Equal(new DateTime(2026, 11, 9), HistoryGrouping.GetDay(new DateTime(2026, 11, 9, 0, 0, 0)));
    }

    [Fact]
    public void Day_UsesLocalTimeForUtcAndOffsets() {
        DateTime utc = new DateTime(2026, 11, 8, 23, 30, 0, DateTimeKind.Utc);
        Assert.Equal(utc.ToLocalTime().Date, HistoryGrouping.GetDay(utc));
        // Zeitpunkt mit fremdem Offset: gruppiert wird nach dem lokalen Tag
        DateTimeOffset tokyo = new DateTimeOffset(2026, 11, 9, 1, 0, 0, TimeSpan.FromHours(9));
        Assert.Equal(tokyo.LocalDateTime.Date, HistoryGrouping.GetDay(tokyo));
    }

    [Fact]
    public void Group_ConsecutiveDays() {
        List<CaptureEntry> entries = new List<CaptureEntry> {
            entry(new DateTime(2026, 11, 9, 9, 0, 0)),
            entry(new DateTime(2026, 11, 9, 0, 0, 0)),
            entry(new DateTime(2026, 11, 8, 23, 59, 59)),
            entry(new DateTime(2026, 11, 1, 12, 0, 0)),
            entry(new DateTime(2026, 11, 1, 11, 0, 0))
        };
        List<HistoryGroup> groups = HistoryGrouping.Group(entries, today);
        Assert.Equal(3, groups.Count);
        Assert.Equal("Heute", groups[0].Label);
        Assert.Equal(2, groups[0].Entries.Count);
        Assert.Equal("Gestern", groups[1].Label);
        Assert.Single(groups[1].Entries);
        Assert.Equal("Sonntag, 1. November 2026", groups[2].Label);
        Assert.Equal(2, groups[2].Entries.Count);
    }

    internal static CaptureEntry entry(DateTime time, string? name = null) {
        string baseName = name ?? time.ToString("yyyy-MM-dd_HH-mm-ss");
        return new CaptureEntry(CaptureFiles.FromBase(@"C:\x", baseName), time);
    }
}

public class HistoryFilterTests {
    private static readonly DateTime now = new DateTime(2026, 11, 9, 15, 0, 0);

    [Fact]
    public void QuickRanges() {
        HistoryFilter todayFilter = HistoryFilter.ForQuickRange(HistoryQuickRange.Today, now);
        Assert.Equal(new DateTime(2026, 11, 9), todayFilter.From);
        Assert.Equal(new DateTime(2026, 11, 9), todayFilter.To);
        Assert.Equal(new DateTime(2026, 11, 3), HistoryFilter.ForQuickRange(HistoryQuickRange.Last7Days, now).From);
        Assert.Null(HistoryFilter.ForQuickRange(HistoryQuickRange.Last7Days, now).To);
        Assert.Equal(new DateTime(2026, 10, 11), HistoryFilter.ForQuickRange(HistoryQuickRange.Last30Days, now).From);
        Assert.True(HistoryFilter.ForQuickRange(HistoryQuickRange.All, now).IsEmpty);
    }

    [Fact]
    public void DateRange_IsInclusiveOnWholeDays() {
        HistoryFilter filter = new HistoryFilter(new DateTime(2026, 11, 3), new DateTime(2026, 11, 5), null);
        Assert.False(filter.Matches(HistoryGroupingTests.entry(new DateTime(2026, 11, 2, 23, 59, 59))));
        Assert.True(filter.Matches(HistoryGroupingTests.entry(new DateTime(2026, 11, 3, 0, 0, 0))));
        Assert.True(filter.Matches(HistoryGroupingTests.entry(new DateTime(2026, 11, 5, 23, 59, 59))));
        Assert.False(filter.Matches(HistoryGroupingTests.entry(new DateTime(2026, 11, 6, 0, 0, 0))));
    }

    [Fact]
    public void Text_MatchesFileNameIgnoringCase() {
        HistoryFilter filter = new HistoryFilter(null, null, "BUG");
        Assert.True(filter.Matches(HistoryGroupingTests.entry(now, "Bericht-bug-123")));
        Assert.False(filter.Matches(HistoryGroupingTests.entry(now, "2026-11-09_15-00-00")));
        Assert.True(new HistoryFilter(null, null, "  ").Matches(HistoryGroupingTests.entry(now)));
    }
}

public class HistoryPagerTests {
    private static List<CaptureEntry> createEntries(int count) {
        DateTime start = new DateTime(2026, 11, 9, 12, 0, 0);
        return Enumerable.Range(0, count).Select(i => HistoryGroupingTests.entry(start.AddMinutes(-30 * i))).ToList();
    }

    [Fact]
    public void LoadsInBlocks() {
        HistoryPager pager = new HistoryPager(createEntries(150), HistoryFilter.All);
        Assert.Equal(60, pager.NextBlock(60).Count);
        Assert.False(pager.IsComplete);
        Assert.Equal(60, pager.NextBlock(60).Count);
        Assert.Equal(30, pager.NextBlock(60).Count);
        Assert.True(pager.IsComplete);
        Assert.Empty(pager.NextBlock(60));
        Assert.Equal(150, pager.Returned);
    }

    [Fact]
    public void ReadsLazily() {
        int read = 0;
        IEnumerable<CaptureEntry> source = createEntries(1000).Select(e => {
            read++;
            return e;
        });
        HistoryPager pager = new HistoryPager(source, HistoryFilter.All);
        pager.NextBlock(60);
        Assert.Equal(60, read);
    }

    [Fact]
    public void FiltersWhilePaging() {
        // 150 Einträge im Abstand von 30 min = gut 3 Tage; nur der 8.11. passt
        HistoryFilter filter = new HistoryFilter(new DateTime(2026, 11, 8), new DateTime(2026, 11, 8), null);
        HistoryPager pager = new HistoryPager(createEntries(150), filter);
        List<CaptureEntry> all = new List<CaptureEntry>();
        while(!pager.IsComplete) all.AddRange(pager.NextBlock(10));
        Assert.Equal(48, all.Count);
        Assert.All(all, e => Assert.Equal(new DateTime(2026, 11, 8), e.Time.Date));
        Assert.Equal(150, pager.Scanned);
    }
}

public class HistoryViewModelTests {
    /// <summary>Leere PNGs (das Aufzählen liest den Inhalt nicht) im Abstand von 20 min, jede 3. ohne JSON.</summary>
    private static (StorageService Storage, DateTime Newest) createStorage(TempFolder temp, int count) {
        SettingsService settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
        settings.Load();
        settings.Settings.StorageFolder = Path.Combine(temp.Path, "ablage");
        StorageService storage = new StorageService(settings, new RenderService());
        DateTime newest = new DateTime(2026, 11, 9, 12, 0, 0);
        for(int i = 0; i < count; i++) {
            DateTime time = newest.AddMinutes(-20 * i);
            string folder = Path.Combine(storage.RootFolder, time.ToString("yyyy"), time.ToString("MM"));
            Directory.CreateDirectory(folder);
            CaptureFiles files = CaptureFiles.FromBase(folder, StorageService.FormatFileName(AppSettings.DefaultFileNamePattern, time));
            File.WriteAllBytes(files.PngPath, new byte[] { 1 });
            if(i % 3 != 0) File.WriteAllText(files.JsonPath, "{}");
        }
        return (storage, newest);
    }

    [Fact]
    public async Task IncrementalLoading_GroupsByDay() {
        using (TempFolder temp = new TempFolder()) {
            (StorageService storage, DateTime newest) = createStorage(temp, 200);
            HistoryViewModel viewModel = new HistoryViewModel(storage, null, () => newest.AddHours(1));
            await viewModel.RefreshAsync();
            Assert.Equal(HistoryViewModel.BlockSize, viewModel.ItemCount);
            Assert.False(viewModel.IsComplete);
            // 12:00 rückwärts in 20-min-Schritten: 37 Einträge am 9.11., der Rest am 8.11.
            Assert.Equal(2, viewModel.GroupCount);
            HistoryHeaderRow first = Assert.IsType<HistoryHeaderRow>(viewModel.Rows[0]);
            Assert.Equal("Heute", first.Label);
            Assert.Equal(37, first.Count);

            await viewModel.LoadMoreAsync();
            Assert.Equal(120, viewModel.ItemCount);
            await viewModel.LoadAllAsync();
            Assert.True(viewModel.IsComplete);
            Assert.Equal(200, viewModel.ItemCount);
            Assert.Equal(viewModel.ItemCount + viewModel.GroupCount, viewModel.Rows.Count);
            // 200 × 20 min: 9.11. 12:00 bis 6.11. 17:40 → 4 Tage
            Assert.Equal(4, viewModel.GroupCount);
            // Reihenfolge: neueste zuerst
            List<HistoryItem> items = viewModel.Items.ToList();
            for(int i = 1; i < items.Count; i++) Assert.True(items[i - 1].Time > items[i].Time);
            // Aufnahmen ohne JSON werden erkannt
            Assert.Equal(67, items.Count(i => i.IsImageOnly));
        }
    }

    [Fact]
    public async Task QuickRangeToday_AndRemoveItems() {
        using (TempFolder temp = new TempFolder()) {
            (StorageService storage, DateTime newest) = createStorage(temp, 100);
            HistoryViewModel viewModel = new HistoryViewModel(storage, null, () => newest.AddHours(1));
            await viewModel.ApplyQuickRangeAsync(HistoryQuickRange.Today);
            await viewModel.LoadAllAsync();
            Assert.Equal(37, viewModel.ItemCount);
            Assert.Equal(1, viewModel.GroupCount);

            await viewModel.ApplyQuickRangeAsync(HistoryQuickRange.All);
            await viewModel.LoadAllAsync();
            Assert.Equal(100, viewModel.ItemCount);
            // alle Einträge eines Tages entfernen → Überschrift verschwindet
            List<HistoryItem> firstDay = viewModel.Items.Where(i => i.Time.Date == newest.Date).ToList();
            viewModel.RemoveItems(firstDay);
            Assert.Equal(63, viewModel.ItemCount);
            Assert.Equal(1, viewModel.GroupCount);
            Assert.IsType<HistoryHeaderRow>(viewModel.Rows[0]);
            Assert.Equal("Gestern", ((HistoryHeaderRow)viewModel.Rows[0]).Label);
        }
    }

    [Fact]
    public async Task AddNewCapture_InsertsAtTopOnce() {
        using (TempFolder temp = new TempFolder()) {
            (StorageService storage, DateTime newest) = createStorage(temp, 10);
            HistoryViewModel viewModel = new HistoryViewModel(storage, null, () => newest.AddHours(1));
            await viewModel.RefreshAsync();
            DateTime time = newest.AddMinutes(5);
            string folder = Path.Combine(storage.RootFolder, time.ToString("yyyy"), time.ToString("MM"));
            CaptureFiles files = CaptureFiles.FromBase(folder, StorageService.FormatFileName(AppSettings.DefaultFileNamePattern, time));
            File.WriteAllBytes(files.PngPath, new byte[] { 1 });
            CaptureEntry entry = storage.GetEntry(files)!;
            viewModel.AddNewCapture(entry);
            viewModel.AddNewCapture(entry);
            Assert.Equal(11, viewModel.ItemCount);
            Assert.Equal(files.PngPath, Assert.IsType<HistoryItem>(viewModel.Rows[1]).Files.PngPath);
            Assert.Equal(11, ((HistoryHeaderRow)viewModel.Rows[0]).Count);
        }
    }
}
