using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

/// <summary>
/// 0.9.0: Hotkeys im Remotedesktop (Hook-Filter mit Remote-Flag, Fensterklassen, Durchreich-Hotkey) und die verzögerte
/// Aufnahme (Einstellungen, Ablauf, Fenster).
/// </summary>
public class RemoteHotkeyTests {
    private const int vkA = 0x41;
    private static readonly HotkeyBinding ctrlShiftA = new HotkeyBinding(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, vkA);
    private static readonly HotkeyBinding print = new HotkeyBinding(HotkeyModifiers.None, HotkeyBinding.VkPrintScreen);

    [Fact]
    public void Filter_RemoteOnly_OnlyWhileRemoteWindowActive() {
        HotkeyFilter filter = new HotkeyFilter(new[] { new HookBinding(HotkeyAction.CaptureSave, ctrlShiftA, HotkeyScope.RemoteOnly) });
        Assert.False(filter.NeedsHook);
        Assert.True(filter.HasRemoteBindings);

        // außerhalb von RDP: durchlassen (WM_HOTKEY löst aus) – weder auslösen noch schlucken
        Assert.False(filter.Process(vkA, true, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, out HotkeyAction? action));
        Assert.Null(action);
        Assert.False(filter.Process(vkA, false, HotkeyModifiers.None, out action));

        filter.RemoteActive = true;
        Assert.True(filter.NeedsHook);
        Assert.True(filter.Process(vkA, true, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, out action));
        Assert.Equal(HotkeyAction.CaptureSave, action);
        // Wiederholung: schlucken, nicht erneut auslösen
        Assert.True(filter.Process(vkA, true, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, out action));
        Assert.Null(action);
        // Remote-Fenster inzwischen weg: das Loslassen der geschluckten Taste wird trotzdem geschluckt
        filter.RemoteActive = false;
        Assert.True(filter.Process(vkA, false, HotkeyModifiers.None, out action));
        Assert.Null(action);

        filter.RemoteActive = true;
        // andere Zusatztasten, andere Tasten: unverändert durch
        Assert.False(filter.Process(vkA, true, HotkeyModifiers.Ctrl, out action));
        Assert.False(filter.Process(vkA, false, HotkeyModifiers.None, out action));
        Assert.False(filter.Process(0x09, true, HotkeyModifiers.Alt, out action));
        Assert.False(filter.Process(0x5B, true, HotkeyModifiers.None, out action));
        Assert.Null(action);
    }

    [Fact]
    public void Filter_SetBindings_KeepsPendingKeyUp() {
        HotkeyFilter filter = new HotkeyFilter(new[] { new HookBinding(HotkeyAction.CancelDelayed, new HotkeyBinding(HotkeyModifiers.None, HotkeyService.VkEscape), HotkeyScope.Always) });
        Assert.True(filter.NeedsHook);
        Assert.True(filter.Process(HotkeyService.VkEscape, true, HotkeyModifiers.None, out HotkeyAction? action));
        Assert.Equal(HotkeyAction.CancelDelayed, action);
        filter.SetBindings(Array.Empty<HookBinding>());
        Assert.False(filter.NeedsHook);
        Assert.True(filter.Process(HotkeyService.VkEscape, false, HotkeyModifiers.None, out action));
        Assert.False(filter.Process(HotkeyService.VkEscape, true, HotkeyModifiers.None, out action));
    }

    [Fact]
    public void BuildHookBindings_NoDoubleTrigger() {
        Dictionary<HotkeyAction, HotkeyBinding> hooked = new Dictionary<HotkeyAction, HotkeyBinding> { [HotkeyAction.CaptureEdit] = print };
        Dictionary<HotkeyAction, HotkeyBinding> registered = new Dictionary<HotkeyAction, HotkeyBinding> {
            [HotkeyAction.CaptureSave] = ctrlShiftA,
            [HotkeyAction.CaptureDelayed] = new HotkeyBinding(HotkeyModifiers.Ctrl, 0x44)
        };
        HotkeyBinding passThrough = new HotkeyBinding(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, HotkeyBinding.VkPrintScreen);

        // ohne Remote-Erfassung: nur der bisherige Hook-Fallback
        List<HookBinding> local = HotkeyService.BuildHookBindings(hooked, registered, passThrough, false, false);
        HookBinding single = Assert.Single(local);
        Assert.Equal(new HookBinding(HotkeyAction.CaptureEdit, print, HotkeyScope.Always), single);

        List<HookBinding> remote = HotkeyService.BuildHookBindings(hooked, registered, passThrough, true, true);
        Assert.Equal(5, remote.Count);
        Assert.Contains(new HookBinding(HotkeyAction.CaptureEdit, print, HotkeyScope.Always), remote);
        Assert.Contains(new HookBinding(HotkeyAction.CaptureSave, ctrlShiftA, HotkeyScope.RemoteOnly), remote);
        Assert.Contains(new HookBinding(HotkeyAction.CaptureDelayed, registered[HotkeyAction.CaptureDelayed], HotkeyScope.RemoteOnly), remote);
        Assert.Contains(new HookBinding(HotkeyAction.RemotePrintScreen, passThrough, HotkeyScope.RemoteOnly), remote);
        Assert.Contains(remote, b => b.Action == HotkeyAction.CancelDelayed && b.Binding.VirtualKey == HotkeyService.VkEscape && b.Scope == HotkeyScope.Always);

        // registrierte Kombination außerhalb von RDP: der Hook lässt sie durch, also nur WM_HOTKEY
        HotkeyFilter filter = new HotkeyFilter(remote);
        Assert.False(filter.Process(vkA, true, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, out HotkeyAction? action));
        Assert.False(filter.Process(HotkeyBinding.VkPrintScreen, true, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, out action));
        Assert.False(filter.Process(HotkeyBinding.VkPrintScreen, false, HotkeyModifiers.None, out action));
        // Hook-Fallback wirkt immer
        Assert.True(filter.Process(HotkeyBinding.VkPrintScreen, true, HotkeyModifiers.None, out action));
        Assert.Equal(HotkeyAction.CaptureEdit, action);
        Assert.True(filter.Process(HotkeyBinding.VkPrintScreen, false, HotkeyModifiers.None, out action));
        // im RDP: genau einmal über den Hook (geschluckt, also kein WM_HOTKEY)
        filter.RemoteActive = true;
        Assert.True(filter.Process(vkA, true, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, out action));
        Assert.Equal(HotkeyAction.CaptureSave, action);
        Assert.True(filter.Process(vkA, false, HotkeyModifiers.None, out action));
        Assert.True(filter.Process(HotkeyBinding.VkPrintScreen, true, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, out action));
        Assert.Equal(HotkeyAction.RemotePrintScreen, action);
    }

    [Theory]
    [InlineData("TscShellContainerClass", new[] { "TscShellContainerClass" })]
    [InlineData(" TscShellContainerClass ; Foo,foo,, Bar ;", new[] { "TscShellContainerClass", "Foo", "Bar" })]
    [InlineData("", new string[0])]
    [InlineData(" ; , ", new string[0])]
    public void ParseWindowClasses(string text, string[] expected) {
        Assert.Equal(expected, RemoteSession.ParseWindowClasses(text));
    }

    [Fact]
    public void IsRemoteWindowClass_IgnoresCase() {
        IReadOnlyList<string> classes = RemoteSession.ParseWindowClasses(RemoteSession.DefaultWindowClasses + ";VMwareRemote");
        Assert.True(RemoteSession.IsRemoteWindowClass("TscShellContainerClass", classes));
        Assert.True(RemoteSession.IsRemoteWindowClass("tscshellcontainerclass", classes));
        Assert.True(RemoteSession.IsRemoteWindowClass("VMwareRemote", classes));
        Assert.False(RemoteSession.IsRemoteWindowClass("Notepad", classes));
        Assert.False(RemoteSession.IsRemoteWindowClass("", classes));
        Assert.False(RemoteSession.IsRemoteWindowClass("TscShellContainerClass", Array.Empty<string>()));
    }

    private static string describe(IReadOnlyList<KeyStroke> strokes) {
        return string.Join(" ", strokes.Select(s => $"{s.VirtualKey:X2}{(s.Up ? "u" : "d")}"));
    }

    [Fact]
    public void PassThroughSequence_NoModifiers_IsPlainPrintScreen() {
        IReadOnlyList<KeyStroke> strokes = RemoteKeySequence.Build(Array.Empty<int>());
        Assert.Equal("2Cd 2Cu", describe(strokes));
        Assert.All(strokes, s => Assert.True(s.Extended));
        Assert.All(strokes, s => Assert.Equal(0x37, s.ScanCode));
    }

    [Fact]
    public void PassThroughSequence_ReleasesAndRestoresHeldModifiers() {
        // Strg+Umschalt: loslassen, Druck, in umgekehrter Reihenfolge wieder drücken
        IReadOnlyList<KeyStroke> strokes = RemoteKeySequence.Build(new[] { ModifierTracker.VkLShift, ModifierTracker.VkLControl });
        Assert.Equal("A2u A0u 2Cd 2Cu A0d A2d", describe(strokes));
        Assert.Equal(0x1D, strokes[0].ScanCode);
        Assert.Equal(0x2A, strokes[1].ScanCode);
        Assert.False(strokes[0].Extended);

        // rechte Strg-Taste ist eine erweiterte Taste
        IReadOnlyList<KeyStroke> right = RemoteKeySequence.Build(new[] { ModifierTracker.VkRControl });
        Assert.Equal("A3u 2Cd 2Cu A3d", describe(right));
        Assert.True(right[0].Extended);
    }

    [Fact]
    public void PassThroughSequence_MasksLoneAltOrWin() {
        // Alt allein: kurzes Strg davor, damit die Sitzung kein einzelnes Alt sieht (Menü)
        Assert.Equal("A2d A2u A4u 2Cd 2Cu A4d", describe(RemoteKeySequence.Build(new[] { ModifierTracker.VkLMenu })));
        Assert.Equal("A2d A2u 5Bu 2Cd 2Cu 5Bd", describe(RemoteKeySequence.Build(new[] { ModifierTracker.VkLWin })));
        Assert.Equal("A0u A2d A2u A4u 2Cd 2Cu A4d A0d", describe(RemoteKeySequence.Build(new[] { ModifierTracker.VkLMenu, ModifierTracker.VkLShift })));
        // mit Strg ist das Loslassen von Strg selbst die Maske
        Assert.Equal("A2u A4u 2Cd 2Cu A4d A2d", describe(RemoteKeySequence.Build(new[] { ModifierTracker.VkLMenu, ModifierTracker.VkLControl })));
    }

    [Fact]
    public void ModifierTracker_TracksLeftRightAndGenericCodes() {
        ModifierTracker tracker = new ModifierTracker();
        tracker.Update(0x11, true);
        tracker.Update(ModifierTracker.VkRShift, true);
        tracker.Update(vkA, true);
        Assert.Equal(new[] { ModifierTracker.VkLControl, ModifierTracker.VkRShift }, tracker.Held);
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, tracker.Modifiers);
        tracker.Update(ModifierTracker.VkLControl, false);
        tracker.Update(ModifierTracker.VkRWin, true);
        Assert.Equal(HotkeyModifiers.Shift | HotkeyModifiers.Win, tracker.Modifiers);
        Assert.True(ModifierTracker.IsModifier(0x12));
        Assert.False(ModifierTracker.IsModifier(HotkeyBinding.VkPrintScreen));
        tracker.Clear();
        Assert.Empty(tracker.Held);
    }

    [Fact]
    public void HotkeyService_RemoteBindingsAndCancelKey() {
        Sta.Run(() => {
            AppSettings settings = new AppSettings {
                HotkeyCaptureEdit = "Ctrl+Alt+Shift+F20",
                HotkeyCaptureSave = "",
                HotkeyHistory = "",
                HotkeyCaptureDelayed = "Ctrl+Alt+Shift+F19",
                HotkeyRemotePrintScreen = "Ctrl+Alt+Shift+F18"
            };
            using (HotkeyService service = new HotkeyService()) {
                service.Apply(settings);
                Assert.Equal(HotkeyState.Registered, service.Status[HotkeyAction.CaptureEdit].State);
                Assert.Equal(HotkeyState.Registered, service.Status[HotkeyAction.CaptureDelayed].State);
                Assert.Equal(HotkeyState.RemoteOnly, service.Status[HotkeyAction.RemotePrintScreen].State);
                Assert.Equal(3, service.FilterBindings.Count);
                Assert.All(service.FilterBindings, b => Assert.Equal(HotkeyScope.RemoteOnly, b.Scope));

                service.SetCancelKeyActive(true);
                Assert.Contains(service.FilterBindings, b => b.Action == HotkeyAction.CancelDelayed && b.Scope == HotkeyScope.Always);
                service.SetCancelKeyActive(false);
                Assert.DoesNotContain(service.FilterBindings, b => b.Action == HotkeyAction.CancelDelayed);

                // ausgesetzt: kein Hook, keine Kombinationen
                service.Suspend();
                Assert.Empty(service.FilterBindings);
                Assert.Equal(HotkeyState.Suspended, service.Status[HotkeyAction.RemotePrintScreen].State);
                service.Resume();
                Assert.Equal(3, service.FilterBindings.Count);

                // ohne Remote-Erfassung: Durchreich-Hotkey aus, Hook nicht nötig
                settings.CaptureHotkeysInRemoteSessions = false;
                service.Apply(settings);
                Assert.Equal(HotkeyState.Off, service.Status[HotkeyAction.RemotePrintScreen].State);
                Assert.Empty(service.FilterBindings);
                Assert.Equal(HotkeyState.Registered, service.Status[HotkeyAction.CaptureEdit].State);
            }
        });
    }

    [Fact]
    public void ConflictChecker_IncludesNewHotkeys() {
        List<(HotkeyAction Action, string Text)> bindings = new List<(HotkeyAction Action, string Text)> {
            (HotkeyAction.CaptureEdit, "PrintScreen"),
            (HotkeyAction.CaptureSave, ""),
            (HotkeyAction.ShowHistory, ""),
            (HotkeyAction.CaptureDelayed, "Ctrl+PrintScreen"),
            (HotkeyAction.RemotePrintScreen, "Strg+Druck")
        };
        List<HotkeyAction> probed = new List<HotkeyAction>();
        List<HotkeyIssue> issues = HotkeyConflictChecker.Check(bindings, (action, binding) => {
            probed.Add(action);
            return (HotkeyProbeResult.Available, 0);
        }, false);
        HotkeyIssue issue = Assert.Single(issues);
        Assert.Equal(HotkeyIssueSeverity.Error, issue.Severity);
        Assert.Contains("Druck an Remotesitzung durchreichen", issue.Message);
        Assert.Contains("Verzögerte Aufnahme", issue.Message);

        // Durchreich-Hotkey wird nie probe-registriert
        bindings[4] = (HotkeyAction.RemotePrintScreen, "Ctrl+Shift+PrintScreen");
        probed.Clear();
        List<HotkeyIssue> taken = HotkeyConflictChecker.Check(bindings, (action, binding) => {
            probed.Add(action);
            return (HotkeyProbeResult.Taken, HotkeyService.ErrorHotkeyAlreadyRegistered);
        }, false);
        Assert.DoesNotContain(taken, i => i.Action == HotkeyAction.RemotePrintScreen);
        Assert.DoesNotContain(HotkeyAction.RemotePrintScreen, probed);
        Assert.Contains(HotkeyAction.CaptureDelayed, probed);
    }
}

public class DelayedCaptureTests {
    private sealed class FakeAutostart : IAutostartService {
        public bool IsEnabled { get; set; }

        public void SetEnabled(bool enabled) {
            IsEnabled = enabled;
        }
    }

    [Fact]
    public void Defaults_AndNormalize() {
        AppSettings defaults = new AppSettings();
        Assert.True(defaults.CaptureHotkeysInRemoteSessions);
        Assert.Equal("TscShellContainerClass", defaults.RemoteSessionWindowClasses);
        Assert.Equal("", defaults.HotkeyRemotePrintScreen);
        Assert.Equal("", defaults.HotkeyCaptureDelayed);
        Assert.Equal(3, defaults.CaptureDelaySeconds);
        Assert.False(defaults.CaptureDelayAskEachTime);
        Assert.Equal(DelayedCaptureActions.Edit, defaults.DelayedCaptureAction);

        AppSettings settings = new AppSettings {
            HotkeyCaptureDelayed = null!,
            HotkeyRemotePrintScreen = null!,
            RemoteSessionWindowClasses = "  ",
            CaptureDelaySeconds = 0,
            DelayedCaptureAction = "quatsch"
        };
        settings.Normalize();
        Assert.Equal("", settings.HotkeyCaptureDelayed);
        Assert.Equal("", settings.HotkeyRemotePrintScreen);
        Assert.Equal(RemoteSession.DefaultWindowClasses, settings.RemoteSessionWindowClasses);
        Assert.Equal(1, settings.CaptureDelaySeconds);
        Assert.Equal(DelayedCaptureActions.Edit, settings.DelayedCaptureAction);

        settings.CaptureDelaySeconds = 500;
        settings.DelayedCaptureAction = DelayedCaptureActions.Save;
        settings.Normalize();
        Assert.Equal(60, settings.CaptureDelaySeconds);
        Assert.Equal(DelayedCaptureActions.Save, settings.DelayedCaptureAction);
    }

    [Fact]
    public void HotkeySignature_CoversAllHotkeySettings() {
        AppSettings settings = new AppSettings();
        List<string> seen = new List<string> { settings.GetHotkeySignature() };
        List<Action> changes = new List<Action> {
            () => settings.HotkeyCaptureEdit = "F9",
            () => settings.HotkeyCaptureSave = "F10",
            () => settings.HotkeyHistory = "F11",
            () => settings.HotkeyCaptureDelayed = "Ctrl+D",
            () => settings.HotkeyRemotePrintScreen = "Ctrl+Shift+PrintScreen",
            () => settings.CaptureHotkeysInRemoteSessions = false,
            () => settings.RemoteSessionWindowClasses = "Foo"
        };
        foreach(Action change in changes) {
            change();
            string signature = settings.GetHotkeySignature();
            Assert.DoesNotContain(signature, seen);
            seen.Add(signature);
        }
        // Verzögerung und Betriebsart wirken ohne Neuregistrierung
        settings.CaptureDelaySeconds = 9;
        settings.CaptureDelayAskEachTime = true;
        Assert.Equal(seen[seen.Count - 1], settings.GetHotkeySignature());
    }

    [Fact]
    public void Controller_FixedDelay_CountsDownAndCaptures() {
        DelayedCaptureController controller = new DelayedCaptureController();
        Assert.Equal(DelayedCaptureEffect.None, controller.Tick());
        Assert.Equal(DelayedCaptureEffect.None, controller.Cancel());

        Assert.Equal(DelayedCaptureEffect.CountdownStarted, controller.HotkeyPressed(false, 3));
        Assert.Equal(DelayedCaptureState.CountingDown, controller.State);
        Assert.Equal(3, controller.Remaining);
        Assert.Equal(DelayedCaptureEffect.CountdownTick, controller.Tick());
        Assert.Equal(2, controller.Remaining);
        Assert.Equal(DelayedCaptureEffect.CountdownTick, controller.Tick());
        Assert.Equal(DelayedCaptureEffect.CaptureNow, controller.Tick());
        Assert.Equal(DelayedCaptureState.Idle, controller.State);
        Assert.False(controller.IsActive);
        Assert.Equal(DelayedCaptureEffect.None, controller.Tick());
    }

    [Fact]
    public void Controller_HotkeyDuringCountdown_CapturesNow_EscCancels() {
        DelayedCaptureController controller = new DelayedCaptureController();
        controller.HotkeyPressed(false, 10);
        controller.Tick();
        Assert.Equal(DelayedCaptureEffect.CaptureNow, controller.HotkeyPressed(false, 10));
        Assert.Equal(DelayedCaptureState.Idle, controller.State);

        controller.HotkeyPressed(false, 10);
        Assert.Equal(DelayedCaptureEffect.Cancelled, controller.Cancel());
        Assert.Equal(DelayedCaptureState.Idle, controller.State);
        Assert.Equal(DelayedCaptureEffect.None, controller.Tick());

        // Bereich wird geklemmt
        controller.HotkeyPressed(false, 0);
        Assert.Equal(1, controller.Remaining);
        Assert.Equal(DelayedCaptureEffect.CaptureNow, controller.Tick());
        controller.HotkeyPressed(false, 1000);
        Assert.Equal(60, controller.Remaining);
    }

    [Fact]
    public void Controller_AskMode_DialogFlow() {
        DelayedCaptureController controller = new DelayedCaptureController();
        Assert.Equal(DelayedCaptureEffect.ShowDialog, controller.HotkeyPressed(true, 4));
        Assert.Equal(DelayedCaptureState.Asking, controller.State);
        Assert.Equal(4, controller.Seconds);
        // Takt im Dialog: nichts
        Assert.Equal(DelayedCaptureEffect.None, controller.Tick());
        // erneuter Hotkey = Dialog bestätigen
        Assert.Equal(DelayedCaptureEffect.ConfirmDialog, controller.HotkeyPressed(true, 4));
        Assert.Equal(DelayedCaptureEffect.CountdownStarted, controller.DialogConfirmed(2));
        Assert.Equal(2, controller.Remaining);
        Assert.Equal(DelayedCaptureEffect.None, controller.DialogConfirmed(5));
        Assert.Equal(DelayedCaptureEffect.CountdownTick, controller.Tick());
        Assert.Equal(DelayedCaptureEffect.CaptureNow, controller.Tick());

        Assert.Equal(DelayedCaptureEffect.ShowDialog, controller.HotkeyPressed(true, 4));
        Assert.Equal(DelayedCaptureEffect.Cancelled, controller.DialogCancelled());
        Assert.Equal(DelayedCaptureState.Idle, controller.State);
        Assert.Equal(DelayedCaptureEffect.None, controller.DialogCancelled());

        // andere Aufnahme während des Dialogs
        controller.HotkeyPressed(true, 4);
        Assert.Equal(DelayedCaptureEffect.Cancelled, controller.Cancel());
    }

    [Fact]
    public void SettingsViewModel_DelayAndRemoteFields() {
        using (TempFolder temp = new TempFolder()) {
            SettingsService service = new SettingsService(Path.Combine(temp.Path, "settings.json"));
            service.Load();
            SettingsViewModel vm = new SettingsViewModel(service, new FakeAutostart(), null, () => false);
            Assert.True(vm.CaptureHotkeysInRemoteSessions);
            Assert.Equal("3", vm.CaptureDelaySecondsText);
            Assert.Equal(DelayedCaptureActions.Edit, vm.DelayedCaptureAction);
            Assert.Equal(2, vm.DelayedCaptureActionChoices.Count);

            vm.CaptureDelaySecondsText = "0";
            Assert.NotNull(vm.CaptureDelaySecondsError);
            Assert.True(vm.HasErrors);
            Assert.False(vm.Apply());
            vm.CaptureDelaySecondsText = "61";
            Assert.True(vm.HasErrors);
            vm.CaptureDelaySecondsText = "7";
            Assert.False(vm.HasErrors);

            vm.HotkeyCaptureDelayed = "Strg+D";
            vm.HotkeyRemotePrintScreen = "Ctrl+Shift+PrintScreen";
            vm.CaptureHotkeysInRemoteSessions = false;
            vm.CaptureDelayAskEachTime = true;
            vm.DelayedCaptureAction = DelayedCaptureActions.Save;
            Assert.True(vm.Apply());

            SettingsService reloaded = new SettingsService(service.FilePath);
            reloaded.Load();
            AppSettings saved = reloaded.Settings;
            Assert.Equal("Ctrl+D", saved.HotkeyCaptureDelayed);
            Assert.Equal("Ctrl+Shift+PrintScreen", saved.HotkeyRemotePrintScreen);
            Assert.False(saved.CaptureHotkeysInRemoteSessions);
            Assert.Equal(7, saved.CaptureDelaySeconds);
            Assert.True(saved.CaptureDelayAskEachTime);
            Assert.Equal(DelayedCaptureActions.Save, saved.DelayedCaptureAction);
            Assert.Equal(RemoteSession.DefaultWindowClasses, saved.RemoteSessionWindowClasses);

            // Duplikat mit einem anderen Hotkey: Fehler, nicht gespeichert
            vm.HotkeyRemotePrintScreen = "Ctrl+D";
            Assert.True(vm.HasHotkeyErrors);
            Assert.False(vm.Apply());
            vm.ClearHotkeyCommand.Execute("RemotePrintScreen");
            Assert.Equal("", vm.HotkeyRemotePrintScreen);
            Assert.False(vm.HasHotkeyErrors);
        }
    }

    [Fact]
    public void DelayDialog_AndCountdownOverlay_XamlLoads() {
        Sta.Run(() => {
            Views.DelayDialog dialog = new Views.DelayDialog(99);
            Assert.Equal("Verzögerte Aufnahme", dialog.Title);
            int? confirmed = null;
            bool cancelled = false;
            dialog.Confirmed += (s, seconds) => confirmed = seconds;
            dialog.Cancelled += (s, e) => cancelled = true;
            // ungültiger/zu großer Vorschlag wird geklemmt
            dialog.Confirm();
            Assert.Equal(60, confirmed);
            Assert.False(cancelled);

            Views.DelayDialog other = new Views.DelayDialog(5);
            other.Cancelled += (s, e) => cancelled = true;
            other.Close();
            Assert.True(cancelled);

            Views.CountdownOverlay overlay = new Views.CountdownOverlay();
            overlay.SetSeconds(4);
            Assert.False(overlay.ShowActivated);
            Assert.True(overlay.Topmost);
            Assert.False(overlay.ShowInTaskbar);
            overlay.Close();
        });
    }
}
