using System.Security.AccessControl;
using System.Security.Principal;
using System.Xml.Linq;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

public class ElevatedAutostartTaskTests {
    private const string sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    private static readonly XNamespace ns = ElevatedAutostartService.TaskNamespace;

    private static string single(XDocument document, string name) {
        return Assert.Single(document.Descendants(ns + name)).Value;
    }

    [Fact]
    public void TaskXml_HasLogonTriggerAndHighestPrivileges() {
        string xml = ElevatedAutostartService.BuildTaskXml(sid, @"C:\Users\Test\AppData\Local\Programs\ScreenKeeper\ScreenKeeper.exe");
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-16\"", xml, StringComparison.OrdinalIgnoreCase);
        XDocument document = XDocument.Parse(xml);
        Assert.Equal(sid, Assert.Single(document.Descendants(ns + "LogonTrigger")).Element(ns + "UserId")?.Value);
        XElement principal = Assert.Single(document.Descendants(ns + "Principal"));
        Assert.Equal(sid, principal.Element(ns + "UserId")?.Value);
        Assert.Equal("InteractiveToken", principal.Element(ns + "LogonType")?.Value);
        Assert.Equal("HighestAvailable", principal.Element(ns + "RunLevel")?.Value);
        Assert.Equal("PT0S", single(document, "ExecutionTimeLimit"));
        Assert.Equal("false", single(document, "DisallowStartIfOnBatteries"));
        Assert.Equal("false", single(document, "StopIfGoingOnBatteries"));
        Assert.Equal("IgnoreNew", single(document, "MultipleInstancesPolicy"));
        Assert.Equal("4", single(document, "Priority"));
        Assert.Equal("true", single(document, "AllowStartOnDemand"));
        Assert.Equal(ElevatedAutostartService.StartedByTaskSwitch, single(document, "Arguments"));
        Assert.Equal(@"C:\Users\Test\AppData\Local\Programs\ScreenKeeper", single(document, "WorkingDirectory"));
        Assert.Equal(ElevatedAutostartService.BuildTaskSecurityDescriptor(sid), single(document, "SecurityDescriptor"));
    }

    [Fact]
    public void TaskXml_EscapesPath() {
        string exe = @"C:\Users\A&B <Böhler>\ScreenKeeper.exe";
        string xml = ElevatedAutostartService.BuildTaskXml(sid, exe);
        Assert.Contains(@"A&amp;B &lt;Böhler&gt;", xml);
        Assert.Equal(exe, ElevatedAutostartService.ParseTaskCommand(xml));
    }

    [Fact]
    public void SecurityDescriptors_AreValidSddl() {
        RawSecurityDescriptor task = new RawSecurityDescriptor(ElevatedAutostartService.BuildTaskSecurityDescriptor(sid));
        Assert.Equal(3, task.DiscretionaryAcl!.Count);
        CommonAce user = Assert.IsType<CommonAce>(task.DiscretionaryAcl[2]);
        Assert.Equal(sid, user.SecurityIdentifier.Value);
        // lesen, ausführen, löschen – aber nicht schreiben und keine Rechte ändern
        const int fileWriteData = 0x2;
        const int writeDac = 0x40000;
        const int delete = 0x10000;
        Assert.Equal(0, user.AccessMask & (fileWriteData | writeDac));
        Assert.NotEqual(0, user.AccessMask & delete);

        RawSecurityDescriptor mutex = new RawSecurityDescriptor(SingleInstance.BuildMutexSddl(sid));
        Assert.Equal(3, mutex.DiscretionaryAcl!.Count);
        Assert.Equal(sid, Assert.IsType<CommonAce>(mutex.DiscretionaryAcl[0]).SecurityIdentifier.Value);
    }

    [Fact]
    public void TaskName_ContainsSid() {
        Assert.Equal($@"ScreenKeeper\Autostart-{sid}", ElevatedAutostartService.GetTaskName(sid));
    }

    [Fact]
    public void ParseTaskCommand_HandlesQuotesAndGarbage() {
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Actions><Exec><Command>\"C:\\A B\\ScreenKeeper.exe\"</Command></Exec></Actions></Task>\r\n";
        Assert.Equal(@"C:\A B\ScreenKeeper.exe", ElevatedAutostartService.ParseTaskCommand(xml));
        // TempFolder leitet das Protokoll um (die Warnung soll nicht ins echte Log)
        using (TempFolder temp = new TempFolder()) {
            Assert.Null(ElevatedAutostartService.ParseTaskCommand("FEHLER: nicht gefunden"));
        }
    }

    [Fact]
    public void QueryTaskXml_UnknownTask_IsNull() {
        // echte, rein lesende Abfrage der Aufgabenplanung ohne Adminrechte
        Assert.Null(ElevatedAutostartService.QueryTaskXml("S-1-5-21-1-2-3-999999"));
    }

    [Fact]
    public void IsSameExecutable_IgnoresCaseAndQuotes() {
        Assert.True(ElevatedAutostartService.IsSameExecutable(@"""C:\Programs\ScreenKeeper\ScreenKeeper.exe""", @"c:\programs\screenkeeper\SCREENKEEPER.EXE"));
        Assert.False(ElevatedAutostartService.IsSameExecutable(@"D:\Alt\ScreenKeeper.exe", @"C:\Programs\ScreenKeeper\ScreenKeeper.exe"));
        Assert.False(ElevatedAutostartService.IsSameExecutable(null, @"C:\Programs\ScreenKeeper\ScreenKeeper.exe"));
        Assert.False(ElevatedAutostartService.IsSameExecutable("", @"C:\Programs\ScreenKeeper\ScreenKeeper.exe"));
    }
}

public class ElevatedAutostartArgumentTests {
    private const string sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    private const string exe = @"C:\Users\Test\AppData\Local\Programs\ScreenKeeper\ScreenKeeper.exe";

    [Fact]
    public void HelperArguments_Roundtrip() {
        foreach(bool enabled in new[] { true, false }) {
            string[] args = ElevatedAutostartService.BuildHelperArguments(enabled, sid, exe).ToArray();
            Assert.True(CommandLineRunner.IsCommandLine(args));
            ElevatedAutostartRequest request = ElevatedAutostartService.ParseHelperArguments(args, path => path == exe);
            Assert.Equal(new ElevatedAutostartRequest(enabled, sid, exe), request);
        }
    }

    [Fact]
    public void StartedByTask_IsNoCommandLine() {
        string[] args = { ElevatedAutostartService.StartedByTaskSwitch };
        Assert.False(CommandLineRunner.IsCommandLine(args));
        Assert.True(ElevatedAutostartService.IsStartedByTask(args));
        Assert.False(ElevatedAutostartService.IsStartedByTask(Array.Empty<string>()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("S-1-5-21-1-2-3-4\"")]
    [InlineData("S-1-5-21-1-2-3-4 & calc")]
    [InlineData("S-1-5-21-1;2")]
    [InlineData("S-1")]
    public void InvalidSid_IsRejected(string value) {
        Assert.False(ElevatedAutostartService.IsValidSid(value));
        string[] args = { ElevatedAutostartService.HelperSwitch, "on", "--sid", value, "--exe", exe };
        Assert.Throws<ArgumentException>(() => ElevatedAutostartService.ParseHelperArguments(args, _ => true));
    }

    [Fact]
    public void CurrentUserSid_IsValid() {
        Assert.True(ElevatedAutostartService.IsValidSid(ElevatedAutostartService.CurrentUserSid));
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) {
            Assert.Equal(identity.User?.Value, ElevatedAutostartService.CurrentUserSid);
        }
    }

    [Theory]
    [InlineData(@"ScreenKeeper.exe")]
    [InlineData(@"..\ScreenKeeper\ScreenKeeper.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Users\Test\ScreenKeeper.exe.bat")]
    [InlineData(@"C:\Users\Test\fehlt\ScreenKeeper.exe")]
    public void InvalidExe_IsRejectedWhenEnabling(string path) {
        string[] args = { ElevatedAutostartService.HelperSwitch, "on", "--sid", sid, "--exe", path };
        Assert.Throws<ArgumentException>(() => ElevatedAutostartService.ParseHelperArguments(args, p => p == exe || p.EndsWith(".bat") || p == @"C:\Windows\System32\cmd.exe"));
    }

    [Fact]
    public void Disabling_AcceptsMissingExe() {
        // Die EXE kann inzwischen fehlen – Abschalten muss trotzdem gehen
        string[] args = { ElevatedAutostartService.HelperSwitch, "OFF", "--sid", sid, "--exe", @"C:\weg\ScreenKeeper.exe" };
        ElevatedAutostartRequest request = ElevatedAutostartService.ParseHelperArguments(args, _ => false);
        Assert.False(request.Enable);
        Assert.Equal(sid, request.Sid);
    }

    [Fact]
    public void ModeAndOptions_AreRequired() {
        Assert.Throws<ArgumentException>(() => ElevatedAutostartService.ParseHelperArguments(new[] { ElevatedAutostartService.HelperSwitch, "vielleicht", "--sid", sid, "--exe", exe }, _ => true));
        Assert.Throws<ArgumentException>(() => ElevatedAutostartService.ParseHelperArguments(new[] { ElevatedAutostartService.HelperSwitch }, _ => true));
        Assert.Throws<ArgumentException>(() => ElevatedAutostartService.ParseHelperArguments(new[] { ElevatedAutostartService.HelperSwitch, "on", "--exe", exe }, _ => true));
        Assert.Throws<ArgumentException>(() => ElevatedAutostartService.ParseHelperArguments(new[] { ElevatedAutostartService.HelperSwitch, "on", "--sid", sid, "--exe" }, _ => true));
    }
}

public class ElevatedAutostartStartTests {
    private sealed class Probe {
        public int Elevated { get; set; }
        public int Enabled { get; set; }
        public int Running { get; set; }
        public int Started { get; set; }

        public bool Run(string[] args, bool? elevated, bool enabled, bool running, bool startResult = true) {
            return ElevatedAutostartService.TryStartViaTask(args,
                () => {
                    Elevated++;
                    return elevated;
                },
                () => {
                    Enabled++;
                    return enabled;
                },
                () => {
                    Running++;
                    return running;
                },
                () => {
                    Started++;
                    return startResult;
                });
        }
    }

    [Fact]
    public void NormalStart_WithElevatedAutostart_StartsTask() {
        Probe probe = new Probe();
        Assert.True(probe.Run(Array.Empty<string>(), false, true, false));
        Assert.Equal(1, probe.Started);
    }

    [Fact]
    public void TaskFailure_ContinuesNormally() {
        Probe probe = new Probe();
        Assert.False(probe.Run(Array.Empty<string>(), false, true, false, false));
        Assert.Equal(1, probe.Started);
    }

    [Fact]
    public void StartedByTask_NeverRedirects() {
        // keine Endlosschleife, auch wenn die Aufgabe nicht erhöht startet (Standardbenutzer)
        Probe probe = new Probe();
        Assert.False(probe.Run(new[] { ElevatedAutostartService.StartedByTaskSwitch }, false, true, false));
        Assert.Equal(0, probe.Elevated + probe.Enabled + probe.Running + probe.Started);
    }

    [Fact]
    public void ElevatedOrUnknown_StartsNormally() {
        Probe probe = new Probe();
        Assert.False(probe.Run(Array.Empty<string>(), true, true, false));
        Assert.False(probe.Run(Array.Empty<string>(), null, true, false));
        Assert.Equal(0, probe.Enabled + probe.Started);
    }

    [Fact]
    public void Disabled_DoesNotCheckInstanceOrStart() {
        Probe probe = new Probe();
        Assert.False(probe.Run(Array.Empty<string>(), false, false, false));
        Assert.Equal(1, probe.Enabled);
        Assert.Equal(0, probe.Running + probe.Started);
    }

    [Fact]
    public void RunningInstance_IsOnlySignalled() {
        Probe probe = new Probe();
        Assert.False(probe.Run(Array.Empty<string>(), false, true, true));
        Assert.Equal(0, probe.Started);
    }

    [Fact]
    public void UpdateScript_RestartsSameExe() {
        // Das Update ersetzt die EXE am selben Pfad (die Aufgabe bleibt gültig) und startet sie aus dem Skript neu,
        // das als Kindprozess die Rechte von ScreenKeeper erbt (erhöht, wenn ScreenKeeper erhöht lief)
        string target = @"C:\Users\Test\AppData\Local\Programs\ScreenKeeper\ScreenKeeper.exe";
        string script = UpdateService.BuildInstallScript(42, @"C:\Temp\neu\ScreenKeeper.exe", target, @"C:\Temp\log.txt");
        Assert.Contains($"$target = '{target}'", script);
        Assert.Contains("Copy-Item -LiteralPath $source -Destination $target -Force", script);
        Assert.Contains("Start-Process -FilePath $target", script);
    }

    [Fact]
    public void ShellOpen_UsesExplorerWhenElevated() {
        System.Diagnostics.ProcessStartInfo elevated = ShellOpen.BuildStartInfo(@"C:\Bilder\Screen Keeper", true);
        Assert.False(elevated.UseShellExecute);
        Assert.Equal(ShellOpen.ExplorerPath, elevated.FileName);
        Assert.Equal(@"C:\Bilder\Screen Keeper", Assert.Single(elevated.ArgumentList));
        System.Diagnostics.ProcessStartInfo normal = ShellOpen.BuildStartInfo("https://github.com/boehla/ScreenKeeper", false);
        Assert.True(normal.UseShellExecute);
        Assert.Equal("https://github.com/boehla/ScreenKeeper", normal.FileName);
    }
}

public class SingleInstanceAclTests {
    [Fact]
    public void Mutex_WithAcl_DetectsSecondInstance() {
        string name = $"Local\\ScreenKeeperTest.{Guid.NewGuid():N}";
        Assert.False(SingleInstance.IsRunning(name));
        using (SingleInstance first = new SingleInstance(name)) {
            Assert.True(first.TryAcquire());
            Assert.True(SingleInstance.IsRunning(name));
            bool secondAcquired = true;
            // anderer Thread, weil ein Mutex für den besitzenden Thread wiedereintrittsfähig ist
            Thread thread = new Thread(() => {
                using (SingleInstance second = new SingleInstance(name)) {
                    secondAcquired = second.TryAcquire();
                }
            });
            thread.Start();
            thread.Join();
            Assert.False(secondAcquired);
        }
        Assert.False(SingleInstance.IsRunning(name));
    }
}

public class ElevatedAutostartViewModelTests {
    private sealed class FakeAutostart : IAutostartService {
        public bool IsEnabled { get; set; }

        public int Calls { get; private set; }

        public void SetEnabled(bool enabled) {
            IsEnabled = enabled;
            Calls++;
        }
    }

    private sealed class Host : ISettingsHost {
        public List<string> Errors { get; } = new List<string>();

        public bool Confirm(string message) {
            return true;
        }

        public string? PickFolder(string title, string initialFolder) {
            return null;
        }

        public void ShowError(string message) {
            Errors.Add(message);
        }
    }

    private sealed class Setup : IDisposable {
        private readonly TempFolder temp = new TempFolder();

        public Setup(bool runValue) {
            Service = new SettingsService(System.IO.Path.Combine(temp.Path, "settings.json"));
            Service.Load();
            Service.Settings.Autostart = runValue;
            Autostart.IsEnabled = runValue;
        }

        public SettingsService Service { get; }

        public FakeAutostart Autostart { get; } = new FakeAutostart();

        public FakeElevatedAutostart Elevated { get; } = new FakeElevatedAutostart();

        public Host Host { get; } = new Host();

        public SettingsViewModel Create() {
            return new SettingsViewModel(Service, Autostart, Elevated, null, () => false) { Host = Host };
        }

        public void Dispose() {
            temp.Dispose();
        }
    }

    [Fact]
    public void NoChange_NoUacCall() {
        using (Setup setup = new Setup(true)) {
            SettingsViewModel viewModel = setup.Create();
            Assert.False(viewModel.ElevatedAutostart);
            Assert.True(viewModel.CanChangeAutostart);
            Assert.Null(viewModel.AutostartToolTip);
            viewModel.RetentionDaysText = "12";
            Assert.True(viewModel.Apply());
            Assert.Empty(setup.Elevated.Calls);
            Assert.Equal(0, setup.Autostart.Calls);
        }
    }

    [Fact]
    public void TurnOn_RemovesRunValue_KeepsPreference_TurnOff_RestoresIt() {
        using (Setup setup = new Setup(true)) {
            SettingsViewModel viewModel = setup.Create();
            viewModel.ElevatedAutostart = true;
            Assert.False(viewModel.CanChangeAutostart);
            Assert.NotNull(viewModel.AutostartToolTip);
            Assert.True(viewModel.Apply());
            Assert.Equal(new[] { true }, setup.Elevated.Calls);
            Assert.True(setup.Service.Settings.ElevatedAutostart);
            Assert.True(setup.Service.Settings.Autostart);
            Assert.False(setup.Autostart.IsEnabled);
            Assert.True(viewModel.ElevatedAutostart);
            Assert.True(viewModel.Autostart);

            // erneut übernehmen: keine weitere UAC-Abfrage
            Assert.True(viewModel.Apply());
            Assert.Single(setup.Elevated.Calls);

            // neu geöffnet: Merker aus den Einstellungen, Autostart zeigt den Wunsch, obwohl der Run-Wert fehlt
            SettingsViewModel reopened = setup.Create();
            Assert.True(reopened.ElevatedAutostart);
            Assert.True(reopened.Autostart);
            Assert.False(reopened.CanChangeAutostart);

            reopened.ElevatedAutostart = false;
            Assert.True(reopened.Apply());
            Assert.Equal(new[] { true, false }, setup.Elevated.Calls);
            Assert.False(setup.Service.Settings.ElevatedAutostart);
            Assert.True(setup.Autostart.IsEnabled);
            Assert.Empty(setup.Host.Errors);
        }
    }

    [Fact]
    public void TurnOff_WithoutAutostartWish_LeavesRunValueAbsent() {
        using (Setup setup = new Setup(false)) {
            SettingsViewModel viewModel = setup.Create();
            viewModel.ElevatedAutostart = true;
            Assert.True(viewModel.Apply());
            viewModel.ElevatedAutostart = false;
            Assert.True(viewModel.Apply());
            Assert.False(setup.Autostart.IsEnabled);
            Assert.Equal(0, setup.Autostart.Calls);
        }
    }

    [Fact]
    public void UacCancelled_RevertsCheckbox_WithoutError() {
        using (Setup setup = new Setup(true)) {
            setup.Elevated.Result = ElevatedAutostartResult.Cancelled;
            SettingsViewModel viewModel = setup.Create();
            viewModel.ElevatedAutostart = true;
            Assert.True(viewModel.Apply());
            Assert.Single(setup.Elevated.Calls);
            Assert.False(viewModel.ElevatedAutostart);
            Assert.True(viewModel.CanChangeAutostart);
            Assert.False(setup.Service.Settings.ElevatedAutostart);
            Assert.True(setup.Autostart.IsEnabled);
            Assert.Empty(setup.Host.Errors);
        }
    }

    [Fact]
    public void Failure_ShowsError_AndKeepsState() {
        using (Setup setup = new Setup(true)) {
            setup.Elevated.Result = ElevatedAutostartResult.Failed;
            SettingsViewModel viewModel = setup.Create();
            viewModel.ElevatedAutostart = true;
            Assert.True(viewModel.Apply());
            Assert.False(viewModel.ElevatedAutostart);
            Assert.False(setup.Service.Settings.ElevatedAutostart);
            Assert.True(setup.Autostart.IsEnabled);
            Assert.Contains("kaputt", Assert.Single(setup.Host.Errors));
        }
    }
}
