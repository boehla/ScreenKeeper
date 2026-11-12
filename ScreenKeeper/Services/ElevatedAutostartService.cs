using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ScreenKeeper.Resources;

namespace ScreenKeeper.Services;

/// <summary>Ergebnis von <see cref="IElevatedAutostartService.SetEnabled"/>.</summary>
public enum ElevatedAutostartResult {
    Success,
    /// <summary>UAC-Abfrage abgelehnt – keine Fehlermeldung, Häkchen zurück.</summary>
    Cancelled,
    Failed
}

/// <summary>Auftrag an den erhöhten Hilfsprozess (<c>--elevated-autostart on|off --sid &lt;SID&gt; --exe &lt;Pfad&gt;</c>).</summary>
public sealed record ElevatedAutostartRequest(bool Enable, string Sid, string ExePath);

public interface IElevatedAutostartService {
    /// <summary>Gibt es die Aufgabe? Fragt die Aufgabenplanung ohne Erhöhung ab (ca. 0,1 s) – nicht im UI-Thread verwenden.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Aufgabe anlegen bzw. löschen: startet die eigene EXE einmal erhöht (UAC-Abfrage) und wartet auf das Ende.
    /// <paramref name="error"/> enthält bei <see cref="ElevatedAutostartResult.Failed"/> die Meldung.
    /// </summary>
    ElevatedAutostartResult SetEnabled(bool enabled, out string error);
}

/// <summary>
/// Autostart mit Adminrechten über die Aufgabenplanung: Aufgabe <c>ScreenKeeper\Autostart-&lt;SID&gt;</c>, Trigger „bei
/// Anmeldung“ dieses Benutzers, höchste Privilegien. Nötig, weil die Druck-Taste wegen UIPI nicht beim (nicht erhöhten)
/// ScreenKeeper ankommt, solange ein Programm mit Adminrechten im Vordergrund ist. Anlegen und Löschen erledigt die
/// eigene EXE erhöht (<see cref="RunHelper"/>); Abfragen und Starten der Aufgabe gehen ohne Erhöhung.
/// </summary>
public sealed class ElevatedAutostartService : IElevatedAutostartService {
    public const string HelperSwitch = "--elevated-autostart";
    /// <summary>Argument der Aufgabe: dieser Start kommt aus der Aufgabe – nie wieder über die Aufgabe umleiten.</summary>
    public const string StartedByTaskSwitch = "--elevated-task";
    public const string TaskFolder = "ScreenKeeper";
    public const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private const int errorCancelled = 1223;
    private static readonly TimeSpan helperTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan schtasksTimeout = TimeSpan.FromSeconds(10);
    private static readonly Regex sidPattern = new Regex(@"^S-1-\d+(-\d+){1,14}$", RegexOptions.CultureInvariant);

    public bool IsEnabled {
        get {
            string? sid = CurrentUserSid;
            return sid != null && QueryTaskXml(sid) != null;
        }
    }

    public ElevatedAutostartResult SetEnabled(bool enabled, out string error) {
        error = "";
        try {
            string exePath = Environment.ProcessPath ?? throw new InvalidOperationException(Strings.UpdateNoProcessPath);
            string sid = CurrentUserSid ?? throw new InvalidOperationException(Strings.Format(Strings.CliInvalidSid, ""));
            ProcessStartInfo info = new ProcessStartInfo(exePath) {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            foreach(string argument in BuildHelperArguments(enabled, sid, exePath)) info.ArgumentList.Add(argument);
            using (Process? process = Process.Start(info)) {
                if(process == null) throw new InvalidOperationException(Strings.ElevatedAutostartTimeout);
                if(!process.WaitForExit(helperTimeout)) throw new TimeoutException(Strings.ElevatedAutostartTimeout);
                if(process.ExitCode == 0) {
                    Log.Info($"Autostart mit Adminrechten {(enabled ? "eingeschaltet" : "ausgeschaltet")} ({exePath})");
                    return ElevatedAutostartResult.Success;
                }
                Log.Warn($"Hilfsprozess für den Autostart mit Adminrechten endete mit Exit-Code {process.ExitCode}");
                error = Strings.Format(Strings.ElevatedAutostartExitCode, process.ExitCode, Log.FilePath);
                return ElevatedAutostartResult.Failed;
            }
        } catch(Win32Exception ex) when(ex.NativeErrorCode == errorCancelled) {
            Log.Info("Autostart mit Adminrechten: UAC-Abfrage abgebrochen");
            return ElevatedAutostartResult.Cancelled;
        } catch(Exception ex) {
            Log.Error(ex, "Autostart mit Adminrechten konnte nicht geändert werden");
            error = ex.Message;
            return ElevatedAutostartResult.Failed;
        }
    }

    /// <summary>SID des angemeldeten Benutzers (null, wenn nicht ermittelbar).</summary>
    public static string? CurrentUserSid {
        get {
            try {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) {
                    return identity.User?.Value;
                }
            } catch(Exception ex) {
                Log.Error(ex, "Benutzer-SID nicht ermittelbar");
                return null;
            }
        }
    }

    public static string SchtasksPath => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    public static string GetTaskName(string sid) {
        return $@"{TaskFolder}\Autostart-{sid}";
    }

    public static bool IsStartedByTask(string[] args) {
        return args.Contains(StartedByTaskSwitch, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Nur Konto-SIDs der Form <c>S-1-…</c> (Ziffern und Bindestriche) – die SID landet im Aufgabennamen und in der XML.</summary>
    public static bool IsValidSid(string? sid) {
        if(sid == null || !sidPattern.IsMatch(sid)) return false;
        try {
            SecurityIdentifier parsed = new SecurityIdentifier(sid);
            return string.Equals(parsed.Value, sid, StringComparison.Ordinal);
        } catch {
            return false;
        }
    }

    public static IReadOnlyList<string> BuildHelperArguments(bool enabled, string sid, string exePath) {
        return new[] { HelperSwitch, enabled ? "on" : "off", "--sid", sid, "--exe", exePath };
    }

    public static ElevatedAutostartRequest ParseHelperArguments(string[] args) {
        return ParseHelperArguments(args, File.Exists);
    }

    /// <summary>
    /// Argumente des Hilfsprozesses prüfen (er läuft erhöht und traut ihnen daher nicht): SID-Format; beim Einschalten
    /// muss <c>--exe</c> ein vollständiger Pfad zu einer vorhandenen Datei namens <c>ScreenKeeper.exe</c> sein.
    /// </summary>
    public static ElevatedAutostartRequest ParseHelperArguments(string[] args, Func<string, bool> fileExists) {
        string mode = getValue(args, HelperSwitch).ToLowerInvariant();
        if(mode != "on" && mode != "off") throw new ArgumentException(Strings.Format(Strings.CliInvalidSwitchValue, HelperSwitch, mode));
        string sid = getValue(args, "--sid");
        if(!IsValidSid(sid)) throw new ArgumentException(Strings.Format(Strings.CliInvalidSid, sid));
        bool enable = mode == "on";
        string exePath = getValue(args, "--exe");
        if(enable) {
            if(!isValidExePath(exePath, fileExists)) throw new ArgumentException(Strings.Format(Strings.CliInvalidExe, exePath));
            exePath = Path.GetFullPath(exePath);
        }
        return new ElevatedAutostartRequest(enable, sid, exePath);
    }

    /// <summary>
    /// Sicherheitsbeschreibung der Aufgabe: Administratoren und SYSTEM voll, der Benutzer darf sie lesen, starten
    /// (<c>schtasks /Run</c> beim manuellen Start) und löschen (Deinstallation ohne Adminrechte), aber nicht ändern.
    /// </summary>
    public static string BuildTaskSecurityDescriptor(string sid) {
        return $"D:(A;;FA;;;BA)(A;;FA;;;SY)(A;;FRFXSD;;;{sid})";
    }

    /// <summary>Aufgaben-XML (UTF-16) für <c>schtasks /Create /XML</c>; Pfad und Texte werden XML-maskiert.</summary>
    public static string BuildTaskXml(string sid, string exePath) {
        XNamespace ns = TaskNamespace;
        XElement task = new XElement(ns + "Task", new XAttribute("version", "1.2"),
            new XElement(ns + "RegistrationInfo",
                new XElement(ns + "Author", "ScreenKeeper"),
                new XElement(ns + "Description", Strings.ElevatedAutostartTaskDescription),
                new XElement(ns + "SecurityDescriptor", BuildTaskSecurityDescriptor(sid))),
            new XElement(ns + "Triggers",
                new XElement(ns + "LogonTrigger",
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "UserId", sid))),
            new XElement(ns + "Principals",
                new XElement(ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(ns + "UserId", sid),
                    new XElement(ns + "LogonType", "InteractiveToken"),
                    new XElement(ns + "RunLevel", "HighestAvailable"))),
            new XElement(ns + "Settings",
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(ns + "StopIfGoingOnBatteries", "false"),
                new XElement(ns + "AllowHardTerminate", "true"),
                new XElement(ns + "StartWhenAvailable", "false"),
                new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                new XElement(ns + "IdleSettings",
                    new XElement(ns + "StopOnIdleEnd", "false"),
                    new XElement(ns + "RestartOnIdle", "false")),
                new XElement(ns + "AllowStartOnDemand", "true"),
                new XElement(ns + "Enabled", "true"),
                new XElement(ns + "Hidden", "false"),
                new XElement(ns + "RunOnlyIfIdle", "false"),
                new XElement(ns + "WakeToRun", "false"),
                new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                new XElement(ns + "Priority", "4")),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(ns + "Exec",
                    new XElement(ns + "Command", exePath),
                    new XElement(ns + "Arguments", StartedByTaskSwitch),
                    new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(exePath) ?? ""))));
        StringBuilder text = new StringBuilder();
        using (StringWriter writer = new StringWriter(text, CultureInfo.InvariantCulture)) {
            new XDocument(new XDeclaration("1.0", "UTF-16", null), task).Save(writer);
        }
        return text.ToString();
    }

    /// <summary>Befehl (EXE-Pfad) aus der Aufgaben-XML; null, wenn nicht lesbar.</summary>
    public static string? ParseTaskCommand(string taskXml) {
        try {
            XDocument document = XDocument.Parse(taskXml.Trim());
            string? command = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value;
            return command == null ? null : Environment.ExpandEnvironmentVariables(command.Trim().Trim('"'));
        } catch(Exception ex) {
            Log.Warn($"Aufgaben-XML nicht lesbar: {ex.Message}");
            return null;
        }
    }

    /// <summary>Zeigt die Aufgabe auf diese EXE? (Groß-/Kleinschreibung egal, Anführungszeichen erlaubt)</summary>
    public static bool IsSameExecutable(string? taskCommand, string exePath) {
        if(string.IsNullOrWhiteSpace(taskCommand)) return false;
        try {
            return string.Equals(Path.GetFullPath(taskCommand.Trim().Trim('"')), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);
        } catch {
            return false;
        }
    }

    /// <summary>
    /// Normaler Start: Ist der Admin-Autostart eingeschaltet, läuft dieser Prozess nicht erhöht, kommt nicht selbst aus der
    /// Aufgabe und läuft noch keine Instanz, wird stattdessen die Aufgabe gestartet (erhöht, ohne UAC). True = erledigt,
    /// dieser Prozess beendet sich. Die Prüfungen laufen in dieser Reihenfolge und nur so weit wie nötig.
    /// </summary>
    public static bool TryStartViaTask(string[] args, Func<bool?> isElevated, Func<bool> isEnabled, Func<bool> isInstanceRunning, Func<bool> startTask) {
        // die per Aufgabe gestartete Instanz nimmt immer den normalen Weg – keine Endlosschleife, auch nicht für
        // Standardbenutzer, bei denen „höchste Privilegien“ keine Adminrechte bedeutet
        if(IsStartedByTask(args)) return false;
        if(isElevated() != false) return false;
        if(!isEnabled()) return false;
        if(isInstanceRunning()) return false;
        return startTask();
    }

    /// <summary>
    /// Aufgabe ohne Erhöhung starten (<c>schtasks /Run</c>), aber nur, wenn sie auf <paramref name="exePath"/> zeigt.
    /// False bei jedem Problem (nur Protokoll) – dann startet ScreenKeeper normal.
    /// </summary>
    public static bool StartTask(string exePath) {
        string? sid = CurrentUserSid;
        if(sid == null || !IsValidSid(sid)) return false;
        if(!checkTaskTarget(sid, exePath)) return false;
        (int exitCode, _, string error) = runSchtasks(new[] { "/Run", "/TN", GetTaskName(sid) });
        if(exitCode != 0) {
            Log.Warn($"Aufgabe {GetTaskName(sid)} ließ sich nicht starten (Exit-Code {exitCode}: {error}) – normaler Start ohne Adminrechte");
            return false;
        }
        Log.Info($"Autostart mit Adminrechten: Aufgabe {GetTaskName(sid)} gestartet, dieser Prozess beendet sich");
        return true;
    }

    /// <summary>
    /// Im Tray-Betrieb bei eingeschaltetem Admin-Autostart: fehlt die Aufgabe oder zeigt sie auf eine andere EXE
    /// (Neuinstallation an anderem Ort), nur ins Protokoll schreiben – nie eine UAC-Abfrage.
    /// </summary>
    public static void LogTaskState(string exePath) {
        string? sid = CurrentUserSid;
        if(sid != null && IsValidSid(sid)) checkTaskTarget(sid, exePath);
    }

    /// <summary>Aufgaben-XML ohne Erhöhung abfragen (<c>schtasks /Query /XML</c>); null, wenn es die Aufgabe nicht gibt oder sie nicht lesbar ist.</summary>
    public static string? QueryTaskXml(string sid) {
        try {
            (int exitCode, byte[] output, _) = runSchtasks(new[] { "/Query", "/TN", GetTaskName(sid), "/XML" });
            return exitCode == 0 ? Encoding.Latin1.GetString(output) : null;
        } catch(Exception ex) {
            Log.Error(ex, "Aufgabe für den Autostart mit Adminrechten nicht abfragbar");
            return null;
        }
    }

    /// <summary>
    /// Erhöhter Hilfsprozess: Aufgabe anlegen (XML in <c>%SystemRoot%\Temp</c>, danach gelöscht) bzw. löschen.
    /// Exit-Code 0 = Erfolg; Fehler werfen eine Ausnahme (Meldung landet im Protokoll).
    /// </summary>
    public static void RunHelper(ElevatedAutostartRequest request) {
        string taskName = GetTaskName(request.Sid);
        if(!request.Enable) {
            if(runSchtasks(new[] { "/Query", "/TN", taskName }).ExitCode != 0) {
                Log.Info($"Aufgabe {taskName} gibt es nicht – nichts zu löschen");
                return;
            }
            ensureSuccess(runSchtasks(new[] { "/Delete", "/TN", taskName, "/F" }));
            Log.Info($"Aufgabe {taskName} gelöscht");
            return;
        }
        string xmlPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", $"ScreenKeeper-Autostart-{Guid.NewGuid():N}.xml");
        try {
            // CreateNew: ein vorab angelegter Fremdeintrag unter diesem Namen wird nie verwendet
            using (FileStream stream = new FileStream(xmlPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                using (StreamWriter writer = new StreamWriter(stream, Encoding.Unicode)) {
                    writer.Write(BuildTaskXml(request.Sid, request.ExePath));
                }
            }
            ensureSuccess(runSchtasks(new[] { "/Create", "/TN", taskName, "/XML", xmlPath, "/F" }));
            Log.Info($"Aufgabe {taskName} angelegt ({request.ExePath})");
        } finally {
            try {
                File.Delete(xmlPath);
            } catch(Exception ex) {
                Log.Warn($"{xmlPath} konnte nicht gelöscht werden: {ex.Message}");
            }
        }
    }

    private static bool checkTaskTarget(string sid, string exePath) {
        string? xml = QueryTaskXml(sid);
        if(xml == null) {
            Log.Warn($"Autostart mit Adminrechten ist eingeschaltet, aber die Aufgabe {GetTaskName(sid)} ist nicht vorhanden oder nicht lesbar");
            return false;
        }
        string? command = ParseTaskCommand(xml);
        if(!IsSameExecutable(command, exePath) && !IsSameExecutable(ParseTaskCommand(decodeOem(xml)), exePath)) {
            Log.Warn($"Aufgabe {GetTaskName(sid)} zeigt auf „{command}“, ScreenKeeper läuft aber aus „{exePath}“ – Einstellung bitte aus- und wieder einschalten");
            return false;
        }
        return true;
    }

    /// <summary>
    /// schtasks gibt die XML umgeleitet als Ein-Byte-Text aus; welche Codepage, ist nicht dokumentiert. Latin-1 lässt die
    /// Bytes unverändert, hier zusätzlich als OEM-Codepage gelesen (Umlaute im Benutzernamen).
    /// </summary>
    private static string decodeOem(string latin1) {
        try {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage).GetString(Encoding.Latin1.GetBytes(latin1));
        } catch {
            return latin1;
        }
    }

    private static void ensureSuccess((int ExitCode, byte[] Output, string Error) result) {
        if(result.ExitCode == 0) return;
        string output = Encoding.Latin1.GetString(result.Output).Trim();
        throw new InvalidOperationException(Strings.Format(Strings.ElevatedAutostartTaskFailed, result.ExitCode, $"{result.Error} {output}".Trim()));
    }

    private static (int ExitCode, byte[] Output, string Error) runSchtasks(IReadOnlyList<string> arguments) {
        ProcessStartInfo info = new ProcessStartInfo(SchtasksPath) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach(string argument in arguments) info.ArgumentList.Add(argument);
        using (Process process = Process.Start(info) ?? throw new InvalidOperationException($"{SchtasksPath} konnte nicht gestartet werden.")) {
            Task<string> error = process.StandardError.ReadToEndAsync();
            using (MemoryStream output = new MemoryStream()) {
                Task copy = process.StandardOutput.BaseStream.CopyToAsync(output);
                if(!process.WaitForExit(schtasksTimeout)) {
                    try {
                        process.Kill();
                    } catch { }
                    throw new TimeoutException($"{SchtasksPath} {string.Join(" ", arguments)}: keine Antwort");
                }
                copy.Wait(schtasksTimeout);
                error.Wait(schtasksTimeout);
                return (process.ExitCode, output.ToArray(), error.IsCompletedSuccessfully ? error.Result.Trim() : "");
            }
        }
    }

    private static bool isValidExePath(string path, Func<string, bool> fileExists) {
        if(string.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(path)) return false;
        if(!string.Equals(Path.GetFileName(path), UpdateService.ExeName, StringComparison.OrdinalIgnoreCase)) return false;
        return fileExists(path);
    }

    private static string getValue(string[] args, string name) {
        for(int i = 0; i < args.Length; i++) {
            if(!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
            if(i + 1 >= args.Length) break;
            return args[i + 1];
        }
        throw new ArgumentException(Strings.Format(Strings.CliMissingArgument, name));
    }
}
