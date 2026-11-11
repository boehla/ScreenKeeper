using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

/// <summary>
/// 0.9.0: Kommentar zu Aufnahmen (Format, Rückgängig, Suche, History) und die AGENTS.md im Ablageordner.
/// </summary>
public class CommentTests {
    private sealed class Host : IEditorHost {
        public PixelPoint GetCursorImagePoint() {
            return new PixelPoint(0, 0);
        }

        public string? AskSaveAsPath(string initialFolder, string fileName) {
            return null;
        }

        public void ShowError(string message) {
        }

        public void CloseEditor() {
        }

        public void DiscardCapture() {
        }
    }

    private sealed class HistoryHost : IHistoryHost {
        public string? Answer { get; set; }

        public string? AskedWith { get; private set; }

        public List<string> Errors { get; } = new List<string>();

        public bool Confirm(string message) {
            return true;
        }

        public void ShowError(string message) {
            Errors.Add(message);
        }

        public string? AskComment(string? comment) {
            AskedWith = comment;
            return Answer;
        }
    }

    private static CaptureDocument createDocument() {
        return new CaptureDocument {
            CapturedAt = new DateTimeOffset(2026, 11, 9, 14, 32, 5, TimeSpan.FromHours(1)),
            Image = "2026-11-09_14-32-05.png",
            VirtualScreen = new PixelRect(0, 0, 400, 300),
            Monitors = new List<MonitorInfo> { new MonitorInfo { Bounds = new PixelRect(0, 0, 400, 300), Primary = true } }
        };
    }

    [Fact]
    public void Comment_IsWritten_AndRoundTrips() {
        CaptureDocument document = createDocument();
        document.Comment = "Fehlermeldung beim Speichern\r\nTicket „4711“ – Größe";
        string json = CaptureDocumentSerializer.Serialize(document);
        Assert.Contains("\"comment\": \"Fehlermeldung beim Speichern\\r\\nTicket „4711“ – Größe\"", json);
        CaptureDocument loaded = CaptureDocumentSerializer.Deserialize(json);
        Assert.Equal(document.Comment, loaded.Comment);
        Assert.True(loaded.HasComment);
        Assert.Equal(1, loaded.FormatVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n ")]
    public void EmptyComment_IsNotWritten(string? comment) {
        CaptureDocument document = createDocument();
        document.Comment = comment;
        Assert.Null(document.Comment);
        Assert.False(document.HasComment);
        string json = CaptureDocumentSerializer.Serialize(document);
        Assert.DoesNotContain("\"comment\"", json);
    }

    [Fact]
    public void OldFile_WithoutComment_LoadsAndStaysWithoutField() {
        string json = "{ \"formatVersion\": 1, \"capturedAt\": \"2026-11-09T14:32:05+01:00\", \"image\": \"a.png\", " +
            "\"virtualScreen\": { \"x\": 0, \"y\": 0, \"width\": 10, \"height\": 10 }, \"monitors\": [], \"windows\": [], " +
            "\"annotations\": [ { \"id\": \"1\", \"type\": \"rectangle\", \"rect\": { \"x\": 1, \"y\": 1, \"width\": 5, \"height\": 5 } } ], \"ocrText\": null }";
        CaptureDocument document = CaptureDocumentSerializer.Deserialize(json);
        Assert.Null(document.Comment);
        Assert.Single(document.Annotations);
        Assert.DoesNotContain("\"comment\"", CaptureDocumentSerializer.Serialize(document));
    }

    [Fact]
    public void BlankCommentInFile_ReadsAsNone() {
        CaptureDocument document = CaptureDocumentSerializer.Deserialize("{ \"formatVersion\": 1, \"comment\": \"   \" }");
        Assert.Null(document.Comment);
    }

    [Fact]
    public void NormalizeComment_TrimsAndUnifiesLineEndings() {
        Assert.Null(CaptureDocument.NormalizeComment(null));
        Assert.Null(CaptureDocument.NormalizeComment(" \n\t "));
        Assert.Equal("a\r\nb\r\nc", CaptureDocument.NormalizeComment("  a\nb\rc \r\n"));
    }

    [Fact]
    public void Copy_KeepsComment() {
        CaptureDocument document = createDocument();
        document.Comment = "bleibt";
        document.Permanent = true;
        StorageService.PrepareCopy(document, "kopie.png");
        Assert.Equal("bleibt", document.Comment);
        Assert.False(document.Permanent);
    }

    [Fact]
    public void Metadata_CarriesComment_AndFilterSearchesIt() {
        CaptureDocument document = createDocument();
        document.Comment = "Rechnung März\r\nTicket 4711";
        CaptureMetadata metadata = CaptureMetadata.FromDocument(document);
        Assert.Equal(document.Comment, metadata.Comment);
        Assert.True(metadata.HasComment);
        Assert.False(CaptureMetadata.None.HasComment);

        CaptureEntry entry = new CaptureEntry(CaptureFiles.FromBase("C:\\x", "2026-11-09_14-32-05"), new DateTime(2026, 11, 9, 14, 32, 5)) { Metadata = metadata };
        Assert.True(new HistoryFilter(null, null, "ticket 4711").Matches(entry));
        Assert.True(new HistoryFilter(null, null, "MÄRZ").Matches(entry));
        Assert.False(new HistoryFilter(null, null, "Angebot").Matches(entry));
        // ohne geladene Kennzeichen zählt nur der Name
        entry.Metadata = null;
        Assert.False(new HistoryFilter(null, null, "4711").Matches(entry));
    }

    [Fact]
    public void CommentText_SingleLineAndPreview() {
        Assert.Equal("", CommentText.SingleLine(null, 10));
        Assert.Equal("eins zwei drei", CommentText.SingleLine("  eins\r\n\r\nzwei   drei ", 100));
        Assert.Equal("abcd…", CommentText.SingleLine("abcdefgh", 5));
        Assert.Equal("a\r\nb", CommentText.Preview(" a\r\nb ", 100));
    }

    [Fact]
    public void Editor_SetComment_IsOneUndoStep() {
        Sta.Run(() => {
            CaptureDocument document = createDocument();
            EditorViewModel vm = new EditorViewModel(document, TestImages.CreatePattern(400, 300), new Host(), new AppSettings());
            List<string?> changed = new List<string?>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (s, e) => changed.Add(e.PropertyName);
            int before = vm.UndoStack.UndoCount;

            Assert.True(vm.SetComment("  Erster Kommentar\nzweite Zeile "));
            Assert.Equal("Erster Kommentar\r\nzweite Zeile", document.Comment);
            Assert.True(vm.HasComment);
            Assert.Contains(nameof(EditorViewModel.HasComment), changed);
            Assert.Contains("Erster Kommentar", vm.CommentToolTip);
            Assert.Equal(before + 1, vm.UndoStack.UndoCount);

            // unverändert: kein weiterer Schritt
            Assert.False(vm.SetComment("Erster Kommentar\r\nzweite Zeile"));
            Assert.Equal(before + 1, vm.UndoStack.UndoCount);

            Assert.True(vm.SetComment("geändert"));
            Assert.True(vm.Undo());
            Assert.Equal("Erster Kommentar\r\nzweite Zeile", document.Comment);
            Assert.True(vm.Undo());
            Assert.Null(document.Comment);
            Assert.False(vm.HasComment);
            Assert.True(vm.Redo());
            Assert.Equal("Erster Kommentar\r\nzweite Zeile", document.Comment);

            // leer = entfernen, ebenfalls ein Schritt
            Assert.True(vm.SetComment(""));
            Assert.Null(document.Comment);
            Assert.True(vm.Undo());
            Assert.Equal("Erster Kommentar\r\nzweite Zeile", document.Comment);
        });
    }

    [Fact]
    public void Editor_CommentCommand_RaisesRequest() {
        Sta.Run(() => {
            EditorViewModel vm = new EditorViewModel(createDocument(), TestImages.CreatePattern(400, 300), new Host(), new AppSettings());
            int requests = 0;
            vm.CommentEditRequested += (s, e) => requests++;
            vm.EditCommentCommand.Execute(null);
            Assert.Equal(1, requests);
            Assert.Equal(0, vm.UndoStack.UndoCount);
        });
    }

    [Fact]
    public void CommentDialog_AndEditorToolbar_XamlLoads() {
        Sta.Run(() => {
            System.Windows.Window dialog = (System.Windows.Window)Activator.CreateInstance(typeof(Views.CommentDialog), BindingFlags.NonPublic | BindingFlags.Instance, null, new object?[] { "Text", null }, null)!;
            Assert.Equal("Kommentar", dialog.Title);
            dialog.Close();
            Views.EditorToolbar toolbar = new Views.EditorToolbar();
            Assert.NotNull(toolbar.Content);
        });
    }

    private static StorageService createStorage(TempFolder temp) {
        SettingsService settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
        settings.Load();
        settings.Settings.StorageFolder = Path.Combine(temp.Path, "ablage");
        return new StorageService(settings, new RenderService());
    }

    private static CaptureFiles createCapture(StorageService storage, bool json) {
        string folder = Path.Combine(storage.RootFolder, "2026", "11");
        Directory.CreateDirectory(folder);
        CaptureFiles files = CaptureFiles.FromBase(folder, "2026-11-09_14-32-05");
        Sta.Run(() => ImageFiles.WritePng(TestImages.CreatePattern(40, 30), files.PngPath));
        if(json) storage.SaveDocument(createDocument(), files.JsonPath);
        return files;
    }

    [Fact]
    public void Storage_SetComment_SavesJson_AndRefreshesMetadata() {
        using (TempFolder temp = new TempFolder()) {
            StorageService storage = createStorage(temp);
            CaptureFiles files = createCapture(storage, true);
            CaptureEntry first = storage.GetEntry(files)!;
            Assert.Equal("", storage.GetMetadata(first).Comment);

            storage.SetComment(files.PngPath, " Ticket 4711 ");
            Assert.Equal("Ticket 4711", storage.LoadDocument(files.JsonPath).Comment);
            Assert.Equal("Ticket 4711", storage.GetMetadata(storage.GetEntry(files)!).Comment);

            storage.SetComment(files.JsonPath, "");
            Assert.Null(storage.LoadDocument(files.JsonPath).Comment);
            Assert.DoesNotContain("\"comment\"", File.ReadAllText(files.JsonPath));
            Assert.Equal("", storage.GetMetadata(storage.GetEntry(files)!).Comment);
        }
    }

    [Fact]
    public void Storage_SetComment_CreatesJsonForImageOnlyCapture() {
        using (TempFolder temp = new TempFolder()) {
            StorageService storage = createStorage(temp);
            CaptureFiles files = createCapture(storage, false);
            storage.SetComment(files.PngPath, "nur Bild");
            Assert.True(File.Exists(files.JsonPath));
            Assert.Equal("nur Bild", storage.LoadDocument(files.JsonPath).Comment);
        }
    }

    [Fact]
    public async Task History_EditComment_SavesAndIsSearchable() {
        using (TempFolder temp = new TempFolder()) {
            StorageService storage = createStorage(temp);
            CaptureFiles files = createCapture(storage, true);
            HistoryHost host = new HistoryHost { Answer = "Angebot Müller\r\nzweite Zeile" };
            HistoryViewModel viewModel = new HistoryViewModel(storage, null, () => new DateTime(2026, 11, 9, 15, 0, 0)) { Host = host };
            await viewModel.RefreshAsync();
            HistoryItem item = Assert.Single(viewModel.Items);
            Assert.False(item.HasComment);

            Assert.True(await viewModel.EditCommentAsync(item));
            Assert.Equal("", host.AskedWith);
            Assert.True(item.HasComment);
            Assert.Equal("Angebot Müller zweite Zeile", item.CommentText);
            Assert.Contains("zweite Zeile", item.CommentToolTip);
            Assert.Equal("Angebot Müller\r\nzweite Zeile", storage.LoadDocument(files.JsonPath).Comment);

            // Dialog zeigt den vorhandenen Kommentar; Abbrechen ändert nichts
            host.Answer = null;
            Assert.False(await viewModel.EditCommentAsync(item));
            Assert.Equal("Angebot Müller\r\nzweite Zeile", host.AskedWith);

            viewModel.FilterText = "müller";
            await viewModel.RefreshAsync();
            await viewModel.LoadAllAsync();
            Assert.Equal(1, viewModel.ItemCount);
            viewModel.FilterText = "Rechnung";
            await viewModel.RefreshAsync();
            await viewModel.LoadAllAsync();
            Assert.Equal(0, viewModel.ItemCount);
            Assert.Empty(host.Errors);
        }
    }
}

public class AgentsFileTests {
    private static string content => AgentsFile.BuildContent("9.9.9");

    [Fact]
    public void Content_HasVersion_AndNoPlaceholder() {
        Assert.Contains("ScreenKeeper 9.9.9", content);
        Assert.DoesNotContain("{{", content);
        Assert.Contains($"ScreenKeeper {CommandLineRunner.GetVersion()}", AgentsFile.BuildContent());
        // CRLF durchgehend
        Assert.DoesNotContain("\n", content.Replace("\r\n", ""));
    }

    [Fact]
    public void Content_MentionsEveryRegisteredAnnotationType() {
        IEnumerable<string> types = typeof(Annotation).GetCustomAttributes<JsonDerivedTypeAttribute>(false)
            .Select(a => a.TypeDiscriminator as string)
            .OfType<string>()
            .ToList();
        Assert.NotEmpty(types);
        Assert.Equal(AnnotationListConverter.KnownTypes.Count, types.Count());
        foreach(string type in types) Assert.True(content.Contains($"### `{type}`"), $"AGENTS.md beschreibt den Annotationstyp \"{type}\" nicht");
    }

    [Fact]
    public void Content_MentionsEveryJsonField() {
        List<Type> types = typeof(Annotation).GetCustomAttributes<JsonDerivedTypeAttribute>(false).Select(a => a.DerivedType).ToList();
        types.AddRange(new[] { typeof(CaptureDocument), typeof(MonitorInfo), typeof(WindowInfo), typeof(CursorInfo), typeof(ForegroundInfo) });
        foreach(Type type in types) {
            foreach(PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if(property.GetCustomAttribute<JsonExtensionDataAttribute>() != null) continue;
                JsonIgnoreAttribute? ignore = property.GetCustomAttribute<JsonIgnoreAttribute>();
                if(ignore != null && ignore.Condition == JsonIgnoreCondition.Always) continue;
                if(property.GetSetMethod() == null) continue;
                string name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                Assert.True(content.Contains($"`{name}`"), $"AGENTS.md erwähnt das Feld \"{name}\" von {type.Name} nicht");
            }
        }
    }

    [Fact]
    public void Content_DocumentsLineCapsAndCursorKinds() {
        foreach(string cap in LineCaps.All) Assert.Contains($"`{cap}`", content);
        foreach(string kind in new[] { CursorKinds.Captured, CursorKinds.Arrow, CursorKinds.Hand, CursorKinds.IBeam }) Assert.Contains($"`{kind}`", content);
        Assert.Contains(CaptureFiles.ThumbExtension, content);
        Assert.Contains(AppSettings.DefaultFileNamePattern, content);
    }

    [Fact]
    public void Ensure_WritesOnlyWhenMissingOrDifferent() {
        using (TempFolder temp = new TempFolder()) {
            string path = AgentsFile.GetPath(temp.Path);
            Assert.True(AgentsFile.Ensure(temp.Path, content));
            Assert.Equal(content, File.ReadAllText(path));
            byte[] bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

            DateTime stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, stamp);
            Assert.False(AgentsFile.Ensure(temp.Path, content));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));

            // neue Programmversion bzw. von Hand geändert: überschreiben
            File.WriteAllText(path, "alt");
            Assert.True(AgentsFile.Ensure(temp.Path, content));
            Assert.Equal(content, File.ReadAllText(path));
            Assert.True(AgentsFile.Ensure(temp.Path, AgentsFile.BuildContent("10.0.0")));
            Assert.Contains("10.0.0", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
        }
    }

    [Fact]
    public void Ensure_DoesNotCreateMissingFolder_AndTryEnsureNeverThrows() {
        using (TempFolder temp = new TempFolder()) {
            string missing = Path.Combine(temp.Path, "fehlt");
            Assert.False(AgentsFile.Ensure(missing, content));
            Assert.False(Directory.Exists(missing));
            Assert.False(AgentsFile.TryEnsure(missing));

            // Ziel ist ein Ordner statt einer Datei: Fehler wird nur protokolliert
            string blocked = Path.Combine(temp.Path, "blockiert");
            Directory.CreateDirectory(Path.Combine(blocked, AgentsFile.FileName));
            Assert.False(AgentsFile.TryEnsure(blocked));
        }
    }

    private static StorageService createStorage(TempFolder temp) {
        SettingsService settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
        settings.Load();
        settings.Settings.StorageFolder = Path.Combine(temp.Path, "ablage");
        return new StorageService(settings, new RenderService());
    }

    [Fact]
    public void Storage_SaveNewCapture_WritesAgentsFile_AndEnumerationIgnoresIt() {
        using (TempFolder temp = new TempFolder()) {
            StorageService storage = createStorage(temp);
            Sta.Run(() => {
                BitmapSource original = TestImages.CreatePattern(40, 30);
                CaptureDocument document = new CaptureDocument {
                    CapturedAt = new DateTimeOffset(2026, 11, 9, 14, 32, 5, TimeSpan.FromHours(1)),
                    VirtualScreen = new PixelRect(0, 0, 40, 30)
                };
                SavedCapture saved = storage.SaveNewCapture(document, original);
                Task.WaitAll(saved.PngTask, saved.ThumbnailTask);
            });
            string agents = AgentsFile.GetPath(storage.RootFolder);
            Assert.True(File.Exists(agents));
            Assert.Equal(AgentsFile.BuildContent(), File.ReadAllText(agents));
            // auch eine (fälschlich) im Monatsordner liegende AGENTS.md ist keine Aufnahme
            File.WriteAllText(Path.Combine(storage.RootFolder, "2026", "11", AgentsFile.FileName), "x");

            List<CaptureEntry> entries = storage.EnumerateCaptures().ToList();
            Assert.Single(entries);
            Assert.EndsWith(".png", entries[0].Files.PngPath);
            Assert.Empty(RetentionService.FindExpired(entries, new DateTime(2026, 11, 10), 30));
            Assert.Single(RetentionService.FindExpired(entries, new DateTime(2027, 11, 10), 30));
        }
    }

    [Fact]
    public void Storage_EnsureAgentsFile_OnlyWhenRootExists_OncePerRoot() {
        using (TempFolder temp = new TempFolder()) {
            StorageService storage = createStorage(temp);
            storage.EnsureAgentsFile();
            Assert.False(Directory.Exists(storage.RootFolder));

            Directory.CreateDirectory(storage.RootFolder);
            storage.EnsureAgentsFile();
            string agents = AgentsFile.GetPath(storage.RootFolder);
            Assert.True(File.Exists(agents));

            // im selben Lauf wird derselbe Ordner nicht erneut geprüft
            File.Delete(agents);
            storage.EnsureAgentsFile();
            Assert.False(File.Exists(agents));

            // neuer Ablageordner (z. B. --folder bzw. Einstellung geändert)
            string other = Path.Combine(temp.Path, "andere");
            Directory.CreateDirectory(other);
            storage.RootFolderOverride = other;
            storage.EnsureAgentsFile();
            Assert.True(File.Exists(AgentsFile.GetPath(other)));
        }
    }
}
