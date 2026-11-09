using System.Windows.Threading;

namespace ScreenKeeper.Services;

/// <summary>
/// Ergebnis eines Aufbewahrungs-Laufs: die (im Probelauf nur gefundenen) abgelaufenen Aufnahmen.
/// </summary>
public sealed record RetentionResult(IReadOnlyList<CaptureEntry> Expired, int Days, DateTime Cutoff, bool DryRun);

/// <summary>
/// Aufbewahrung: Aufnahmen älter als <see cref="AppSettings.RetentionDays"/> Tage (alle drei Dateien) in den Papierkorb.
/// Läuft beim Start und danach einmal täglich (stündliche Prüfung per Timer); 0 Tage = aus.
/// </summary>
public sealed class RetentionService : IDisposable {
    private static readonly TimeSpan checkInterval = TimeSpan.FromHours(1);

    private readonly IStorageService storage;
    private readonly ISettingsService settings;
    private DispatcherTimer? timer;
    private DateTime lastRunDay = DateTime.MinValue;
    private int lastRunDays;
    private string lastRunRoot = "";
    private bool running;

    public RetentionService(IStorageService storage, ISettingsService settings) {
        this.storage = storage;
        this.settings = settings;
    }

    /// <summary>Nach einem Lauf, der Aufnahmen gelöscht hat (im UI-Thread).</summary>
    public event EventHandler<RetentionResult>? Completed;

    /// <summary>Stichtag: Aufnahmen vor diesem Zeitpunkt sind abgelaufen.</summary>
    public static DateTime GetCutoff(DateTime now, int days) {
        return now.AddDays(-days);
    }

    /// <summary>
    /// Die abgelaufenen Einträge (Aufnahmezeit vor <see cref="GetCutoff"/>); bei <paramref name="days"/> ≤ 0 keine.
    /// </summary>
    public static List<CaptureEntry> FindExpired(IEnumerable<CaptureEntry> entries, DateTime now, int days) {
        List<CaptureEntry> expired = new List<CaptureEntry>();
        if(days <= 0) return expired;
        DateTime cutoff = GetCutoff(now, days);
        foreach(CaptureEntry entry in entries) {
            if(entry.Time < cutoff) expired.Add(entry);
        }
        return expired;
    }

    /// <summary>
    /// Ein Lauf (blockierend). Im Probelauf (<paramref name="dryRun"/>) wird nur ermittelt, nichts gelöscht.
    /// </summary>
    public RetentionResult Run(DateTime now, int days, bool dryRun) {
        DateTime cutoff = GetCutoff(now, days);
        if(days <= 0) return new RetentionResult(Array.Empty<CaptureEntry>(), days, cutoff, dryRun);
        // Monatsordner nach dem Stichtag gar nicht erst lesen
        List<CaptureEntry> expired = FindExpired(storage.EnumerateCaptures(null, cutoff), now, days);
        if(!dryRun && expired.Count > 0) storage.DeleteToRecycleBin(expired.Select(e => e.Files));
        string action = dryRun ? "gefunden (Probelauf, nichts gelöscht)" : "in den Papierkorb verschoben";
        Log.Info($"Aufbewahrung {days} Tage (Stichtag {cutoff:yyyy-MM-dd HH:mm}): {expired.Count} Aufnahme(n) {action}");
        return new RetentionResult(expired, days, cutoff, dryRun);
    }

    /// <summary>
    /// Startet den Timer und führt sofort einen Lauf aus, falls eingeschaltet. Im UI-Thread aufrufen.
    /// </summary>
    public void Start() {
        timer = new DispatcherTimer { Interval = checkInterval };
        timer.Tick += (s, e) => Reevaluate();
        timer.Start();
        Reevaluate();
    }

    /// <summary>
    /// Läuft, wenn heute (bzw. mit der aktuellen Dauer oder dem aktuellen Ablageordner) noch nicht gelaufen –
    /// nach Einstellungsänderungen und vom Timer aufgerufen.
    /// </summary>
    public void Reevaluate() {
        int days = settings.Settings.RetentionDays;
        if(days <= 0 || running) return;
        string root = storage.RootFolder;
        DateTime today = DateTime.Today;
        if(lastRunDay == today && lastRunDays == days && string.Equals(lastRunRoot, root, StringComparison.OrdinalIgnoreCase)) return;
        lastRunDay = today;
        lastRunDays = days;
        lastRunRoot = root;
        runInBackground(DateTime.Now, days);
    }

    public void Dispose() {
        timer?.Stop();
        timer = null;
    }

    private async void runInBackground(DateTime now, int days) {
        running = true;
        try {
            RetentionResult result = await StaTask.Run(() => Run(now, days, false));
            if(result.Expired.Count > 0) Completed?.Invoke(this, result);
        } catch(Exception ex) {
            Log.Error(ex, "Aufbewahrung fehlgeschlagen");
        } finally {
            running = false;
        }
    }
}
