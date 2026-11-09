using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

public enum HotkeyIssueSeverity {
    /// <summary>Speichern nicht möglich.</summary>
    Error,
    /// <summary>Hinweis; Speichern trotzdem möglich.</summary>
    Warning
}

public sealed record HotkeyIssue(HotkeyAction Action, HotkeyIssueSeverity Severity, string Message);

/// <summary>
/// Konfliktprüfung der Hotkeys beim Speichern der Einstellungen (UI-frei):
/// ungültig oder ohne Modifizierer → Fehler; zwei eigene Hotkeys gleich → Fehler;
/// Probe-Registrierung schlägt fehl (von anderem Programm belegt) → Hinweis (bei Druck greift der Tastatur-Hook);
/// Druck ohne Modifizierer bei aktiver Windows-Einstellung „Druck öffnet Bildschirmausschnitt“ → Hinweis.
/// </summary>
public static class HotkeyConflictChecker {
    /// <param name="bindings">Hotkey-Text je Aktion (leer = aus).</param>
    /// <param name="probe">Probe-Registrierung (null = nicht prüfen); liefert das Ergebnis und den Win32-Fehler.</param>
    /// <param name="snippingEnabled">Windows-Einstellung „Druck-Taste öffnet Bildschirmausschnitt“ ist aktiv.</param>
    public static List<HotkeyIssue> Check(IReadOnlyList<(HotkeyAction Action, string Text)> bindings, Func<HotkeyAction, HotkeyBinding, (HotkeyProbeResult Result, int Error)>? probe, bool snippingEnabled) {
        List<HotkeyIssue> issues = new List<HotkeyIssue>();
        List<(HotkeyAction Action, HotkeyBinding Binding)> parsed = new List<(HotkeyAction Action, HotkeyBinding Binding)>();
        foreach((HotkeyAction action, string text) in bindings) {
            if(string.IsNullOrWhiteSpace(text)) continue;
            if(!HotkeyBinding.TryParse(text, out HotkeyBinding? binding) || binding == null) {
                issues.Add(new HotkeyIssue(action, HotkeyIssueSeverity.Error, Strings.Format(Strings.HotkeyErrorInvalid, GetActionName(action), text)));
                continue;
            }
            if(binding.NeedsModifier) {
                issues.Add(new HotkeyIssue(action, HotkeyIssueSeverity.Error, Strings.Format(Strings.HotkeyErrorNeedsModifier, GetActionName(action), binding.ToDisplayString())));
                continue;
            }
            parsed.Add((action, binding));
        }

        HashSet<HotkeyAction> duplicates = new HashSet<HotkeyAction>();
        for(int i = 0; i < parsed.Count; i++) {
            for(int j = i + 1; j < parsed.Count; j++) {
                if(parsed[i].Binding != parsed[j].Binding) continue;
                duplicates.Add(parsed[i].Action);
                duplicates.Add(parsed[j].Action);
                issues.Add(new HotkeyIssue(parsed[j].Action, HotkeyIssueSeverity.Error, Strings.Format(Strings.HotkeyErrorDuplicate, GetActionName(parsed[i].Action), GetActionName(parsed[j].Action), parsed[i].Binding.ToDisplayString())));
            }
        }

        foreach((HotkeyAction action, HotkeyBinding binding) in parsed) {
            if(duplicates.Contains(action)) continue;
            if(probe != null) {
                (HotkeyProbeResult result, int error) = probe(action, binding);
                if(result != HotkeyProbeResult.Available) {
                    string text;
                    if(binding.IsPrintScreen) {
                        text = Strings.Format(Strings.HotkeyWarningTakenPrint, GetActionName(action), binding.ToDisplayString());
                    } else if(result == HotkeyProbeResult.Taken) {
                        text = Strings.Format(Strings.HotkeyWarningTaken, GetActionName(action), binding.ToDisplayString());
                    } else {
                        text = Strings.Format(Strings.HotkeyWarningFailed, GetActionName(action), binding.ToDisplayString(), error);
                    }
                    issues.Add(new HotkeyIssue(action, HotkeyIssueSeverity.Warning, text));
                }
            }
            if(snippingEnabled && binding.IsPrintScreen && binding.Modifiers == HotkeyModifiers.None) {
                issues.Add(new HotkeyIssue(action, HotkeyIssueSeverity.Warning, Strings.Format(Strings.HotkeyWarningSnipping, GetActionName(action))));
            }
        }
        return issues;
    }

    public static string GetActionName(HotkeyAction action) {
        switch(action) {
            case HotkeyAction.CaptureEdit: return Strings.SettingsHotkeyCaptureEdit;
            case HotkeyAction.CaptureSave: return Strings.SettingsHotkeyCaptureSave;
            case HotkeyAction.ShowHistory: return Strings.SettingsHotkeyHistory;
        }
        return action.ToString();
    }
}
