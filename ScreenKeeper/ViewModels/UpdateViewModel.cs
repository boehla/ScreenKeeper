using System.Globalization;
using System.Windows.Input;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Anzeige des Update-Dienstes für History-Statusleiste und Einstellungen: unaufdringlicher Link, wenn eine neue Version
/// da ist, Fortschritt bzw. Fehler als Text, „Jetzt prüfen“ mit Ergebnis inline. Nach Gebrauch <see cref="Detach"/>.
/// </summary>
public sealed class UpdateViewModel : ViewModelBase {
    private readonly UpdateService service;

    public UpdateViewModel(UpdateService service) {
        this.service = service;
        service.Changed += onChanged;
        InstallCommand = new RelayCommand(() => _ = service.InstallAsync(), () => service.Available != null && !service.IsBusy);
        CheckCommand = new RelayCommand(() => _ = service.CheckAsync(true), () => !service.IsBusy);
    }

    public ICommand InstallCommand { get; }

    public ICommand CheckCommand { get; }

    public bool IsAvailable => service.Available != null;

    /// <summary>Link sichtbar: Update vorhanden und gerade nichts in Arbeit.</summary>
    public bool ShowLink => service.Available != null && !service.IsBusy;

    /// <summary>„Version 0.8.0 verfügbar – installieren“.</summary>
    public string LinkText => service.Available != null ? Strings.Format(Strings.UpdateAvailableLink, service.Available.Version) : "";

    /// <summary>Fortschritt bzw. Fehler der Installation; leer = nichts zu melden.</summary>
    public string StatusText => service.StatusText;

    public bool HasStatus => service.StatusText.Length > 0;

    /// <summary>Ergebnis der letzten Prüfung (Einstellungen).</summary>
    public string CheckResultText => service.CheckResultText;

    /// <summary>„Zuletzt geprüft: …“ bzw. „noch nicht geprüft“.</summary>
    public string LastCheckText {
        get {
            if(!service.LastCheck.HasValue) return Strings.UpdateNeverChecked;
            return Strings.Format(Strings.UpdateLastCheck, service.LastCheck.Value.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));
        }
    }

    public string CurrentVersionText => Strings.Format(Strings.UpdateInstalledVersion, service.CurrentVersion);

    public void Detach() {
        service.Changed -= onChanged;
    }

    private void onChanged(object? sender, EventArgs e) {
        OnPropertyChanged(string.Empty);
        CommandManager.InvalidateRequerySuggested();
    }
}
