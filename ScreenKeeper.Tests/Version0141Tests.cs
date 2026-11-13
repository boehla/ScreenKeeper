using ScreenKeeper.Resources;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

/// <summary>
/// 0.14.1: „Autostart mit Adminrechten“ nur anbieten, wenn das Konto überhaupt Adminrechte bekommen kann. Bei
/// Standardbenutzern liefert „höchste Privilegien“ still ein normales Token – das Häkchen ist dann ausgegraut, außer die
/// Aufgabe ist schon angelegt (Ausschalten bleibt möglich).
/// </summary>
public class ElevationCapabilityTests {
    private const int typeDefault = 1;
    private const int typeFull = 2;
    private const int typeLimited = 3;

    [Theory]
    [InlineData(typeFull)]
    [InlineData(typeLimited)]
    public void SplitToken_CanElevate(int type) {
        Assert.True(ElevatedAutostartService.CanUserElevate(false, type, () => throw new InvalidOperationException("nicht fragen")));
    }

    [Fact]
    public void DefaultToken_StandardUser_CannotElevate() {
        Assert.False(ElevatedAutostartService.CanUserElevate(false, typeDefault, () => false));
    }

    [Fact]
    public void DefaultToken_AdministratorsMember_CanElevate() {
        // UAC aus oder eingebauter Administrator: kein geteiltes Token, aber Mitglied der Administratoren
        Assert.True(ElevatedAutostartService.CanUserElevate(false, typeDefault, () => true));
    }

    [Fact]
    public void AlreadyElevated_CanElevate() {
        Assert.True(ElevatedAutostartService.CanUserElevate(true, typeDefault, () => false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    public void Unknown_DoesNotBlock(int? type) {
        Assert.True(ElevatedAutostartService.CanUserElevate(null, type, () => false));
    }

    [Fact]
    public void CurrentUser_DoesNotThrow() {
        // nur Smoke-Test: das Ergebnis hängt vom Konto ab, auf dem die Tests laufen
        ElevatedAutostartService.CurrentUserCanElevate();
    }
}

public class ElevatedAutostartAvailabilityViewModelTests {
    private sealed class FakeAutostart : IAutostartService {
        public bool IsEnabled { get; set; }

        public void SetEnabled(bool enabled) {
            IsEnabled = enabled;
        }
    }

    private static SettingsViewModel create(SettingsService service, FakeElevatedAutostart elevated) {
        return new SettingsViewModel(service, new FakeAutostart(), elevated, null, () => false);
    }

    [Fact]
    public void Admin_CheckBoxEnabled_NormalHint() {
        using (TempFolder temp = new TempFolder()) {
            SettingsService service = new SettingsService(Path.Combine(temp.Path, "settings.json"));
            service.Load();
            SettingsViewModel viewModel = create(service, new FakeElevatedAutostart { CanElevate = true });
            Assert.True(viewModel.CanChangeElevatedAutostart);
            Assert.Equal(Strings.SettingsElevatedAutostartHint, viewModel.ElevatedAutostartHint);
        }
    }

    [Fact]
    public void StandardUser_TaskOff_CheckBoxDisabled() {
        using (TempFolder temp = new TempFolder()) {
            SettingsService service = new SettingsService(Path.Combine(temp.Path, "settings.json"));
            service.Load();
            SettingsViewModel viewModel = create(service, new FakeElevatedAutostart { CanElevate = false });
            Assert.False(viewModel.CanChangeElevatedAutostart);
            Assert.Equal(Strings.SettingsElevatedAutostartNoAdmin, viewModel.ElevatedAutostartHint);
        }
    }

    [Fact]
    public void StandardUser_TaskOn_CanTurnOff_ThenDisabled() {
        using (TempFolder temp = new TempFolder()) {
            SettingsService service = new SettingsService(Path.Combine(temp.Path, "settings.json"));
            service.Load();
            service.Settings.ElevatedAutostart = true;
            FakeElevatedAutostart elevated = new FakeElevatedAutostart { CanElevate = false, IsEnabled = true };
            SettingsViewModel viewModel = create(service, elevated);
            Assert.True(viewModel.CanChangeElevatedAutostart);
            Assert.Equal(Strings.SettingsElevatedAutostartNoAdminCanDisable, viewModel.ElevatedAutostartHint);

            // abgehakt, aber noch nicht übernommen: wieder anhakbar
            viewModel.ElevatedAutostart = false;
            Assert.True(viewModel.CanChangeElevatedAutostart);

            Assert.True(viewModel.Apply());
            Assert.Equal(new[] { false }, elevated.Calls);
            Assert.False(service.Settings.ElevatedAutostart);
            Assert.False(viewModel.CanChangeElevatedAutostart);
            Assert.Equal(Strings.SettingsElevatedAutostartNoAdmin, viewModel.ElevatedAutostartHint);
        }
    }
}
