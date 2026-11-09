using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using ScreenKeeper.Resources;

namespace ScreenKeeper.Services;

/// <summary>
/// Eine veröffentlichte Version: Versionsnummer (ohne „v“), Release-Seite und – nur bei einem Single-File-Build – das
/// passende ZIP (<see cref="AssetUrl"/>/<see cref="AssetName"/>; null = nur die Release-Seite öffnen).
/// </summary>
public sealed record UpdateInfo(string Version, string ReleaseUrl, string? AssetUrl, string? AssetName);

/// <summary>Ergebnis einer Prüfung: installierte Version, neueste Version und ob diese neuer ist.</summary>
public sealed record UpdateCheckResult(string CurrentVersion, UpdateInfo Latest, bool IsNewer);

/// <summary>Was der Update-Dienst gerade tut.</summary>
public enum UpdateActivity {
    Idle,
    Checking,
    Downloading,
    Installing
}

/// <summary>
/// Sucht im Hintergrund nach neuen Versionen (GitHub-Releases, höchstens einmal je 24 h; Zeitpunkt in
/// <c>%APPDATA%\ScreenKeeper\update.json</c>) und installiert sie mit einem Klick: ZIP laden, entpacken, ein kleines
/// PowerShell-Skript ersetzt nach dem Programmende die EXE und startet neu. Nie Dialoge – Ergebnis und Fehler stehen in
/// <see cref="StatusText"/> bzw. <see cref="CheckResultText"/>. Öffentliche Methoden im UI-Thread aufrufen.
/// </summary>
public sealed class UpdateService : IDisposable {
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/boehla/ScreenKeeper/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/boehla/ScreenKeeper/releases";
    public const string ExeName = "ScreenKeeper.exe";

    private static readonly TimeSpan checkInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan startDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan timerInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan requestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan downloadTimeout = TimeSpan.FromMinutes(10);
    private static readonly HttpClient http = createClient();

    private readonly ISettingsService settings;
    private readonly string stateFile;
    private readonly string currentVersion;
    private DispatcherTimer? timer;
    private UpdateInfo? available;
    private UpdateActivity activity;
    private string statusText = "";
    private string checkResultText = "";
    private DateTimeOffset? lastCheck;

    public UpdateService(ISettingsService settings) : this(settings, AppPaths.UpdateStateFile, CommandLineRunner.GetVersion()) {
    }

    public UpdateService(ISettingsService settings, string stateFile, string currentVersion) {
        this.settings = settings;
        this.stateFile = stateFile;
        this.currentVersion = currentVersion;
        loadState();
    }

    /// <summary>Nach jeder Änderung von Zustand, Fortschritt oder Ergebnis (im UI-Thread).</summary>
    public event EventHandler? Changed;

    public string CurrentVersion => currentVersion;

    /// <summary>Neuere Version, falls vorhanden.</summary>
    public UpdateInfo? Available => available;

    public UpdateActivity Activity => activity;

    public bool IsBusy => activity != UpdateActivity.Idle;

    /// <summary>Fortschritt bzw. Fehler der Installation (für die Statusleiste der History); leer = nichts zu melden.</summary>
    public string StatusText => statusText;

    /// <summary>Ergebnis der letzten Prüfung („aktuell“, „Version x verfügbar“, Fehler) für das Einstellungsfenster.</summary>
    public string CheckResultText => checkResultText;

    /// <summary>Zeitpunkt der letzten Prüfung (auch einer fehlgeschlagenen); null = noch nie.</summary>
    public DateTimeOffset? LastCheck => lastCheck;

    /// <summary>Vor dem Beenden für ein Update: Editor schließen, Schreibvorgänge abwarten (vom Programm gesetzt).</summary>
    public Action? PrepareExit { get; set; }

    /// <summary>Programm beenden, damit das Skript die EXE ersetzen kann (vom Programm gesetzt).</summary>
    public Action? Exit { get; set; }

    /// <summary>Läuft die App als einzelne EXE (veröffentlichter Build)? Entwicklungs-Builds aus <c>bin\</c> nicht.</summary>
    public static bool IsSingleFile {
        get {
            // Absicht: im Single-File-Build ist der Pfad leer – genau das wird hier erkannt
#pragma warning disable IL3000
            return string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location);
#pragma warning restore IL3000
        }
    }

    /// <summary>Bringt die App ihre .NET-Laufzeit selbst mit (liegt die Laufzeit unter dem Programmordner)?</summary>
    public static bool IsSelfContained {
        get {
            try {
                string runtime = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory());
                string baseFolder = Path.GetFullPath(AppContext.BaseDirectory);
                return runtime.StartsWith(baseFolder, StringComparison.OrdinalIgnoreCase);
            } catch(Exception ex) {
                Log.Warn($"Laufzeit-Ordner nicht ermittelbar: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Versionsnummer aus einem Tag (<c>v0.7.0</c>) oder einer Programmversion (<c>0.7.0+abc</c>, <c>0.8.0-beta</c>);
    /// fehlende Stellen zählen als 0. Null, wenn nicht lesbar.
    /// </summary>
    public static Version? ParseVersion(string? text) {
        if(string.IsNullOrWhiteSpace(text)) return null;
        string value = text.Trim();
        if(value.StartsWith('v') || value.StartsWith('V')) value = value.Substring(1);
        int cut = value.IndexOfAny(new[] { '-', '+', ' ' });
        if(cut >= 0) value = value.Substring(0, cut);
        if(int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int major)) return new Version(major, 0, 0, 0);
        if(!Version.TryParse(value, out Version? version)) return null;
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }

    /// <summary>Ist <paramref name="latest"/> neuer als <paramref name="current"/>? Unlesbare Versionen: nein.</summary>
    public static bool IsNewer(string? latest, string? current) {
        Version? latestVersion = ParseVersion(latest);
        Version? currentVersion = ParseVersion(current);
        return latestVersion != null && currentVersion != null && latestVersion > currentVersion;
    }

    /// <summary>Dateiname des Release-ZIPs (wie in <c>.github/workflows/release.yml</c>).</summary>
    public static string GetAssetName(string version, bool selfContained) {
        return selfContained ? $"ScreenKeeper-{version}-win-x64.zip" : $"ScreenKeeper-{version}-win-x64-framework-dependent.zip";
    }

    /// <summary>
    /// Release-JSON der GitHub-API auswerten: Version, Release-Seite und – nur bei einem Single-File-Build – das passende
    /// ZIP (mit Laufzeit bzw. framework-abhängig). Fehlt das ZIP, bleibt nur die Release-Seite.
    /// </summary>
    public static UpdateInfo SelectAsset(string json, bool isSingleFile, bool isSelfContained) {
        using (JsonDocument document = JsonDocument.Parse(json)) {
            return SelectAsset(document.RootElement, isSingleFile, isSelfContained);
        }
    }

    public static UpdateInfo SelectAsset(JsonElement release, bool isSingleFile, bool isSelfContained) {
        string? tag = getString(release, "tag_name");
        if(ParseVersion(tag) == null) throw new InvalidDataException(Strings.Format(Strings.UpdateInvalidRelease, tag ?? "–"));
        string version = tag!.Trim().TrimStart('v', 'V');
        string releaseUrl = getString(release, "html_url") ?? ReleasesPageUrl;
        if(!isHttps(releaseUrl)) releaseUrl = ReleasesPageUrl;
        if(!isSingleFile) return new UpdateInfo(version, releaseUrl, null, null);
        string wanted = GetAssetName(version, isSelfContained);
        if(release.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array) {
            foreach(JsonElement asset in assets.EnumerateArray()) {
                string? name = getString(asset, "name");
                string? url = getString(asset, "browser_download_url");
                if(string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase) && url != null && isHttps(url)) return new UpdateInfo(version, releaseUrl, url, name);
            }
        }
        return new UpdateInfo(version, releaseUrl, null, null);
    }

    /// <summary>Prüfergebnis aus dem Release-JSON und der installierten Version.</summary>
    public static UpdateCheckResult Evaluate(string json, string currentVersion, bool isSingleFile, bool isSelfContained) {
        UpdateInfo latest = SelectAsset(json, isSingleFile, isSelfContained);
        return new UpdateCheckResult(currentVersion, latest, IsNewer(latest.Version, currentVersion));
    }

    /// <summary>
    /// Timer starten: erste Prüfung nach 30 s, danach stündlich nachsehen, ob 24 h seit der letzten Prüfung vergangen sind.
    /// </summary>
    public void Start() {
        if(timer != null) return;
        timer = new DispatcherTimer { Interval = startDelay };
        timer.Tick += onTimerTick;
        timer.Start();
        string last = lastCheck.HasValue ? lastCheck.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "nie";
        if(settings.Settings.CheckForUpdates) {
            Log.Info($"Update-Prüfung geplant in {startDelay.TotalSeconds:0} s (letzte Prüfung: {last}, installiert {currentVersion})");
        } else {
            Log.Info("Update-Prüfung ist ausgeschaltet");
        }
        if(available != null) raiseChanged();
    }

    /// <summary>Nach Einstellungsänderungen: eingeschaltet und fällig → prüfen.</summary>
    public void Reevaluate() {
        if(timer == null) return;
        _ = checkIfDueAsync();
    }

    /// <summary>
    /// Jetzt prüfen (auch wenn die letzte Prüfung kürzer als 24 h her ist). <paramref name="remember"/>: Zeitpunkt und
    /// Ergebnis in <c>update.json</c> merken. Null bei Fehler (Text in <see cref="CheckResultText"/>).
    /// </summary>
    public async Task<UpdateCheckResult?> CheckAsync(bool remember) {
        if(IsBusy) return null;
        activity = UpdateActivity.Checking;
        checkResultText = Strings.UpdateChecking;
        raiseChanged();
        try {
            string json = await fetchLatestReleaseAsync();
            UpdateCheckResult result = Evaluate(json, currentVersion, IsSingleFile, IsSelfContained);
            lastCheck = DateTimeOffset.Now;
            available = result.IsNewer ? result.Latest : null;
            checkResultText = result.IsNewer ? Strings.Format(Strings.UpdateAvailable, result.Latest.Version, currentVersion) : Strings.Format(Strings.UpdateUpToDate, currentVersion);
            if(remember) saveState(json);
            string asset = result.Latest.AssetName ?? "keins (nur Release-Seite)";
            Log.Info($"Update-Prüfung: installiert {currentVersion}, neueste {result.Latest.Version} – {(result.IsNewer ? $"Update verfügbar, Paket {asset}" : "kein Update")}");
            return result;
        } catch(Exception ex) {
            lastCheck = DateTimeOffset.Now;
            checkResultText = Strings.Format(Strings.UpdateCheckFailed, describe(ex));
            if(remember) saveState(null);
            Log.Warn($"Update-Prüfung fehlgeschlagen: {ex.GetType().Name}: {ex.Message}");
            return null;
        } finally {
            activity = UpdateActivity.Idle;
            raiseChanged();
        }
    }

    /// <summary>
    /// Update installieren: Single-File-Build → ZIP laden, entpacken, EXE prüfen, Skript starten, Programm beenden.
    /// Sonst (Entwicklungs-Build oder kein passendes Paket) die Release-Seite im Browser öffnen. Fehler nur im Statustext.
    /// </summary>
    public async Task InstallAsync() {
        UpdateInfo? info = available;
        if(info == null || IsBusy) return;
        if(!IsSingleFile || info.AssetUrl == null || info.AssetName == null) {
            OpenReleasePage(info.ReleaseUrl);
            return;
        }
        try {
            string targetExe = Environment.ProcessPath ?? throw new InvalidOperationException(Strings.UpdateNoProcessPath);
            ensureWritable(Path.GetDirectoryName(targetExe) ?? "");
            activity = UpdateActivity.Downloading;
            statusText = Strings.Format(Strings.UpdateDownloading, info.Version, 0);
            raiseChanged();
            string folder = Path.Combine(AppPaths.UpdateFolder, info.Version);
            Directory.CreateDirectory(folder);
            string zipPath = Path.Combine(folder, info.AssetName);
            Progress<int> progress = new Progress<int>(percent => {
                statusText = Strings.Format(Strings.UpdateDownloading, info.Version, percent);
                raiseChanged();
            });
            string assetUrl = info.AssetUrl;
            await Task.Run(() => downloadAsync(assetUrl, zipPath, progress));

            activity = UpdateActivity.Installing;
            statusText = Strings.UpdateExtracting;
            raiseChanged();
            string newExe = await Task.Run(() => ExtractUpdate(zipPath, Path.Combine(folder, "files")));
            string scriptPath = Path.Combine(Path.GetTempPath(), $"ScreenKeeper-Update-{Environment.ProcessId}.ps1");
            string script = BuildInstallScript(Environment.ProcessId, newExe, targetExe, Path.Combine(AppPaths.UpdateFolder, "install-log.txt"));
            File.WriteAllText(scriptPath, script, new UTF8Encoding(true));
            statusText = Strings.Format(Strings.UpdateRestarting, info.Version);
            raiseChanged();
            Log.Info($"Update {info.Version} geladen ({newExe}), Programm wird beendet und neu gestartet");
            PrepareExit?.Invoke();
            startScript(scriptPath);
            Exit?.Invoke();
        } catch(Exception ex) {
            Log.Error(ex, $"Update {info.Version} konnte nicht installiert werden");
            activity = UpdateActivity.Idle;
            statusText = Strings.Format(Strings.UpdateInstallFailed, describe(ex));
            raiseChanged();
        }
    }

    /// <summary>Release-Seite im Standardbrowser öffnen.</summary>
    public void OpenReleasePage(string? url = null) {
        string target = url != null && isHttps(url) ? url : ReleasesPageUrl;
        try {
            ProcessStartInfo info = new ProcessStartInfo(target) { UseShellExecute = true };
            using (Process? process = Process.Start(info)) {
            }
            Log.Info($"Release-Seite geöffnet: {target}");
        } catch(Exception ex) {
            Log.Error(ex, "Release-Seite konnte nicht geöffnet werden");
            statusText = Strings.Format(Strings.UpdateInstallFailed, ex.Message);
            raiseChanged();
        }
    }

    /// <summary>
    /// ZIP nach <paramref name="targetFolder"/> entpacken (vorher leeren) und die enthaltene <c>ScreenKeeper.exe</c> liefern.
    /// </summary>
    public static string ExtractUpdate(string zipPath, string targetFolder) {
        if(Directory.Exists(targetFolder)) Directory.Delete(targetFolder, true);
        ZipFile.ExtractToDirectory(zipPath, targetFolder);
        string exe = Path.Combine(targetFolder, ExeName);
        if(File.Exists(exe)) return exe;
        string? nested = Directory.EnumerateFiles(targetFolder, ExeName, SearchOption.AllDirectories).FirstOrDefault();
        return nested ?? throw new InvalidDataException(Strings.UpdateNoExe);
    }

    /// <summary>
    /// PowerShell-Skript: wartet auf das Ende von Prozess <paramref name="processId"/>, kopiert die neue EXE über die alte
    /// (Wiederholung bis 30 s), startet das Programm neu und löscht sich selbst.
    /// </summary>
    public static string BuildInstallScript(int processId, string sourceExe, string targetExe, string logPath) {
        StringBuilder script = new StringBuilder();
        script.AppendLine("# ScreenKeeper-Update: wartet auf das Programmende, ersetzt die EXE und startet neu");
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine($"$processId = {processId.ToString(CultureInfo.InvariantCulture)}");
        script.AppendLine($"$source = {quote(sourceExe)}");
        script.AppendLine($"$target = {quote(targetExe)}");
        script.AppendLine($"$log = {quote(logPath)}");
        script.AppendLine("function Write-UpdateLog($text) {");
        script.AppendLine("    try { Add-Content -LiteralPath $log -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $text) } catch { }");
        script.AppendLine("}");
        script.AppendLine("Write-UpdateLog \"Warte auf Prozess $processId\"");
        script.AppendLine("try { Wait-Process -Id $processId -Timeout 60 } catch { }");
        script.AppendLine("$deadline = (Get-Date).AddSeconds(30)");
        script.AppendLine("$copied = $false");
        script.AppendLine("while(-not $copied) {");
        script.AppendLine("    try {");
        script.AppendLine("        Copy-Item -LiteralPath $source -Destination $target -Force");
        script.AppendLine("        $copied = $true");
        script.AppendLine("    } catch {");
        script.AppendLine("        if((Get-Date) -gt $deadline) { Write-UpdateLog \"EXE nicht ersetzt: $($_.Exception.Message)\"; break }");
        script.AppendLine("        Start-Sleep -Milliseconds 500");
        script.AppendLine("    }");
        script.AppendLine("}");
        script.AppendLine("if($copied) { Write-UpdateLog \"EXE ersetzt: $target\" }");
        script.AppendLine("try { Start-Process -FilePath $target } catch { Write-UpdateLog \"Neustart fehlgeschlagen: $($_.Exception.Message)\" }");
        script.AppendLine("Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue");
        return script.ToString();
    }

    public void Dispose() {
        timer?.Stop();
        timer = null;
    }

    private async void onTimerTick(object? sender, EventArgs e) {
        if(timer != null) timer.Interval = timerInterval;
        try {
            await checkIfDueAsync();
        } catch(Exception ex) {
            Log.Error(ex, "Update-Prüfung fehlgeschlagen");
        }
    }

    private async Task checkIfDueAsync() {
        if(!settings.Settings.CheckForUpdates || IsBusy) return;
        if(lastCheck.HasValue && DateTimeOffset.Now - lastCheck.Value < checkInterval && lastCheck.Value <= DateTimeOffset.Now) return;
        await CheckAsync(true);
    }

    private async Task<string> fetchLatestReleaseAsync() {
        using (CancellationTokenSource timeout = new CancellationTokenSource(requestTimeout)) {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl)) {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                using (HttpResponseMessage response = await http.SendAsync(request, timeout.Token)) {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync(timeout.Token);
                }
            }
        }
    }

    private static async Task downloadAsync(string url, string path, IProgress<int> progress) {
        string partial = path + ".part";
        using (CancellationTokenSource timeout = new CancellationTokenSource(downloadTimeout)) {
            using (HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token)) {
                response.EnsureSuccessStatusCode();
                long? total = response.Content.Headers.ContentLength;
                using (Stream input = await response.Content.ReadAsStreamAsync(timeout.Token)) {
                    using (FileStream output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true)) {
                        byte[] buffer = new byte[81920];
                        long done = 0;
                        int lastPercent = -1;
                        int read;
                        while((read = await input.ReadAsync(buffer, timeout.Token)) > 0) {
                            await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                            done += read;
                            if(total.HasValue && total.Value > 0) {
                                int percent = (int)Math.Min(100, done * 100 / total.Value);
                                if(percent != lastPercent) {
                                    lastPercent = percent;
                                    progress.Report(percent);
                                }
                            }
                        }
                    }
                }
            }
        }
        File.Move(partial, path, true);
    }

    private static void startScript(string scriptPath) {
        string powerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        ProcessStartInfo info = new ProcessStartInfo(File.Exists(powerShell) ? powerShell : "powershell.exe") {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach(string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", scriptPath }) {
            info.ArgumentList.Add(argument);
        }
        using (Process? process = Process.Start(info)) {
            if(process == null) throw new InvalidOperationException(Strings.UpdateScriptFailed);
        }
    }

    /// <summary>Lässt sich die EXE ersetzen? Sonst lieber gleich melden, statt nach dem Neustart unverändert dazustehen.</summary>
    private static void ensureWritable(string folder) {
        string probe = Path.Combine(folder, $".screenkeeper-update-{Guid.NewGuid():N}.tmp");
        try {
            using (FileStream stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) {
            }
        } catch(Exception ex) {
            throw new InvalidOperationException(Strings.Format(Strings.UpdateFolderNotWritable, folder), ex);
        }
    }

    private void loadState() {
        try {
            if(!File.Exists(stateFile)) return;
            JsonNode? state = JsonNode.Parse(File.ReadAllText(stateFile, Encoding.UTF8));
            if(state is not JsonObject root) return;
            if(root["lastCheck"] is JsonValue checkValue && checkValue.TryGetValue(out string? checkText)
                && DateTimeOffset.TryParse(checkText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset checkedAt)) {
                lastCheck = checkedAt;
            }
            if(root["release"] is JsonObject release) {
                UpdateCheckResult result = Evaluate(release.ToJsonString(), currentVersion, IsSingleFile, IsSelfContained);
                available = result.IsNewer ? result.Latest : null;
                if(result.IsNewer) checkResultText = Strings.Format(Strings.UpdateAvailable, result.Latest.Version, currentVersion);
            }
        } catch(Exception ex) {
            Log.Warn($"{stateFile} nicht lesbar: {ex.Message}");
        }
    }

    /// <summary>
    /// Zeitpunkt und (gekürzt) die gefundene Version merken: Tag, Release-Seite, Pakete. Ohne <paramref name="releaseJson"/>
    /// (Prüfung fehlgeschlagen) bleibt das letzte Ergebnis erhalten.
    /// </summary>
    private void saveState(string? releaseJson) {
        try {
            JsonObject state = new JsonObject {
                ["lastCheck"] = (lastCheck ?? DateTimeOffset.Now).ToString("o", CultureInfo.InvariantCulture)
            };
            JsonObject? release = releaseJson != null ? trimRelease(releaseJson) : readStoredRelease();
            if(release != null) state["release"] = release;
            AtomicFile.WriteAllText(stateFile, state.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        } catch(Exception ex) {
            Log.Warn($"{stateFile} konnte nicht geschrieben werden: {ex.Message}");
        }
    }

    private JsonObject? readStoredRelease() {
        try {
            if(!File.Exists(stateFile)) return null;
            JsonObject? root = JsonNode.Parse(File.ReadAllText(stateFile, Encoding.UTF8)) as JsonObject;
            return root?["release"]?.DeepClone() as JsonObject;
        } catch(Exception ex) {
            Log.Warn($"{stateFile} nicht lesbar: {ex.Message}");
            return null;
        }
    }

    private static JsonObject trimRelease(string releaseJson) {
        JsonObject result = new JsonObject();
        using (JsonDocument document = JsonDocument.Parse(releaseJson)) {
            JsonElement release = document.RootElement;
            result["tag_name"] = getString(release, "tag_name");
            result["html_url"] = getString(release, "html_url");
            JsonArray assets = new JsonArray();
            if(release.TryGetProperty("assets", out JsonElement list) && list.ValueKind == JsonValueKind.Array) {
                foreach(JsonElement asset in list.EnumerateArray()) {
                    assets.Add(new JsonObject {
                        ["name"] = getString(asset, "name"),
                        ["browser_download_url"] = getString(asset, "browser_download_url")
                    });
                }
            }
            result["assets"] = assets;
        }
        return result;
    }

    private void raiseChanged() {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string describe(Exception ex) {
        if(ex is OperationCanceledException) return Strings.Format(Strings.UpdateTimeout, (int)requestTimeout.TotalSeconds);
        if(ex is HttpRequestException request && request.StatusCode.HasValue) return $"HTTP {(int)request.StatusCode.Value} {request.StatusCode.Value}";
        return ex.Message;
    }

    private static string? getString(JsonElement element, string name) {
        if(element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return null;
        return value.GetString();
    }

    private static bool isHttps(string url) {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps;
    }

    private static string quote(string text) {
        return "'" + text.Replace("'", "''") + "'";
    }

    private static HttpClient createClient() {
        HttpClient client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ScreenKeeper", CommandLineRunner.GetVersion()));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/boehla/ScreenKeeper)"));
        return client;
    }
}
