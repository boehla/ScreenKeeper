namespace ScreenKeeper.Services;

public enum DelayedCaptureState {
    /// <summary>Nichts läuft.</summary>
    Idle,
    /// <summary>Der Dialog „Aufnahme in … Sekunden“ ist offen.</summary>
    Asking,
    /// <summary>Der Countdown läuft.</summary>
    CountingDown
}

/// <summary>Was die Oberfläche nach einem Ereignis tun soll.</summary>
public enum DelayedCaptureEffect {
    /// <summary>Nichts.</summary>
    None,
    /// <summary>Dialog öffnen (Vorschlag = <see cref="DelayedCaptureController.Seconds"/>).</summary>
    ShowDialog,
    /// <summary>Den offenen Dialog mit seinem aktuellen Wert bestätigen (erneuter Hotkey).</summary>
    ConfirmDialog,
    /// <summary>Countdown beginnt: Overlay zeigen, Esc abfangen, Takt starten.</summary>
    CountdownStarted,
    /// <summary>Restzeit hat sich geändert.</summary>
    CountdownTick,
    /// <summary>Jetzt aufnehmen (Countdown abgelaufen oder erneuter Hotkey).</summary>
    CaptureNow,
    /// <summary>Abgebrochen (Esc, Tray-Klick, andere Aufnahme, Dialog abgebrochen).</summary>
    Cancelled
}

/// <summary>
/// Ablauf der verzögerten Aufnahme (UI-frei, testbar): Leerlauf → (Dialog →) Countdown → Aufnahme.
/// Ereignisse: Hotkey, Dialog bestätigt/abgebrochen, Takt (1 s), Abbruch. Die Oberfläche (App) setzt die Effekte um.
/// </summary>
public sealed class DelayedCaptureController {
    public DelayedCaptureState State { get; private set; } = DelayedCaptureState.Idle;

    /// <summary>Gewählte bzw. vorgeschlagene Verzögerung in Sekunden.</summary>
    public int Seconds { get; private set; }

    /// <summary>Verbleibende Sekunden im Countdown.</summary>
    public int Remaining { get; private set; }

    public bool IsActive => State != DelayedCaptureState.Idle;

    public static int ClampSeconds(int seconds) {
        return Math.Clamp(seconds, AppSettings.MinCaptureDelaySeconds, AppSettings.MaxCaptureDelaySeconds);
    }

    /// <summary>
    /// Delay-Hotkey (oder Tray-Eintrag): im Leerlauf Dialog bzw. Countdown starten, bei offenem Dialog bestätigen, im
    /// Countdown sofort auslösen.
    /// </summary>
    public DelayedCaptureEffect HotkeyPressed(bool askEachTime, int defaultSeconds) {
        switch(State) {
            case DelayedCaptureState.Asking:
                return DelayedCaptureEffect.ConfirmDialog;
            case DelayedCaptureState.CountingDown:
                reset();
                return DelayedCaptureEffect.CaptureNow;
        }
        Seconds = ClampSeconds(defaultSeconds);
        if(askEachTime) {
            State = DelayedCaptureState.Asking;
            return DelayedCaptureEffect.ShowDialog;
        }
        return start(Seconds);
    }

    public DelayedCaptureEffect DialogConfirmed(int seconds) {
        if(State != DelayedCaptureState.Asking) return DelayedCaptureEffect.None;
        return start(ClampSeconds(seconds));
    }

    public DelayedCaptureEffect DialogCancelled() {
        if(State != DelayedCaptureState.Asking) return DelayedCaptureEffect.None;
        reset();
        return DelayedCaptureEffect.Cancelled;
    }

    /// <summary>Eine Sekunde ist vergangen.</summary>
    public DelayedCaptureEffect Tick() {
        if(State != DelayedCaptureState.CountingDown) return DelayedCaptureEffect.None;
        Remaining--;
        if(Remaining > 0) return DelayedCaptureEffect.CountdownTick;
        reset();
        return DelayedCaptureEffect.CaptureNow;
    }

    /// <summary>Esc, Klick aufs Tray-Symbol oder eine andere Aufnahme: Dialog bzw. Countdown abbrechen.</summary>
    public DelayedCaptureEffect Cancel() {
        if(State == DelayedCaptureState.Idle) return DelayedCaptureEffect.None;
        reset();
        return DelayedCaptureEffect.Cancelled;
    }

    private DelayedCaptureEffect start(int seconds) {
        Seconds = seconds;
        Remaining = seconds;
        State = DelayedCaptureState.CountingDown;
        return DelayedCaptureEffect.CountdownStarted;
    }

    private void reset() {
        State = DelayedCaptureState.Idle;
        Remaining = 0;
    }
}
