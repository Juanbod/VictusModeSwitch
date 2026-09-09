using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VictusModeSwitch.Tests;

public sealed class CoreTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(0, 1)]
    public void TogglePerformance_UsesOnlyCoreModes(int sourceValue, int expectedValue)
    {
        var source = (AppMode)sourceValue;
        var expected = (AppMode)expectedValue;

        Assert.Equal(expected, source.TogglePerformance());
    }

    [Theory]
    [InlineData("\"Gaming\"")]
    [InlineData("3")]
    public void AppModeConverter_MigratesLegacyGamingToPerformance(string json)
    {
        var mode = JsonSerializer.Deserialize<AppMode>(json);

        Assert.Equal(AppMode.Performance, mode);
    }

    [Theory]
    [InlineData("v2.0.0", 2, 0, 0)]
    [InlineData("2.1.3-beta.1", 2, 1, 3)]
    [InlineData("V10.4.12+build", 10, 4, 12)]
    public void ParseVersion_AcceptsReleaseTags(string tag, int major, int minor, int patch)
    {
        Assert.Equal(new Version(major, minor, patch), UpdateService.ParseVersion(tag));
    }

    [Fact]
    public void ParseChecksum_AcceptsSha256FileFormat()
    {
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        Assert.Equal(hash.ToUpperInvariant(), UpdateService.ParseChecksum($"{hash}  VictusModeSwitch-Setup.exe\n"));
        Assert.Null(UpdateService.ParseChecksum("not-a-hash"));
    }

    [Fact]
    public void VerifyInstaller_AcceptsMatchingSidecar()
    {
        using var fixture = new InstallerFixture();

        UpdateService.VerifyInstaller(fixture.InstallerPath);
    }

    [Fact]
    public void VerifyInstaller_RejectsModifiedInstaller()
    {
        using var fixture = new InstallerFixture();
        File.AppendAllText(fixture.InstallerPath, "modified");

        Assert.Throws<InvalidOperationException>(() =>
            UpdateService.VerifyInstaller(fixture.InstallerPath));
    }

    [Fact]
    public void Localizer_ProvidesAllCoreModeNames()
    {
        var localizer = new Localizer("ru");

        Assert.Equal("Eco", localizer.ModeName(AppMode.Eco));
        Assert.Equal("Стандартный", localizer.ModeName(AppMode.Standard));
        Assert.Equal("Performance", localizer.ModeName(AppMode.Performance));
    }

    [Fact]
    public void AppSettings_MigratesLegacyPowerTuning()
    {
        const string json = """
            {
              "CurrentMode": "Gaming",
              "Experimental": {
                "WindowsPowerTuningEnabled": true,
                "EcoAcMaximumProcessor": 75,
                "EcoBatteryMaximumProcessor": 55
              }
            }
            """;
        var options = new JsonSerializerOptions
        {
            Converters = { new AppModeJsonConverter(), new JsonStringEnumConverter() }
        };

        var settings = JsonSerializer.Deserialize<AppSettings>(json, options)!;
        settings.Normalize();

        Assert.Equal(AppMode.Performance, settings.CurrentMode);
        Assert.True(settings.PowerTuning.Enabled);
        Assert.Equal(75, settings.PowerTuning.EcoAcMaximumProcessor);
        Assert.Equal(55, settings.PowerTuning.EcoBatteryMaximumProcessor);
    }

    [Fact]
    public void AppSettings_NormalizesMissingEcoBehaviorState()
    {
        const string json = """
            {
              "EcoBehavior": null,
              "EcoBehaviorBackup": null
            }
            """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        settings.Normalize();

        Assert.NotNull(settings.EcoBehavior);
        Assert.NotNull(settings.EcoBehaviorBackup);
        Assert.False(settings.EcoBehavior.LimitDisplayRefreshRate);
        Assert.False(settings.EcoBehavior.LimitNvidiaFrameRate);
        Assert.False(settings.EcoBehavior.DisableTurboBoost);
    }

    [Fact]
    public void AppSettings_NormalizesMissingKeyboardShortcut()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"KeyboardShortcut\":null}")!;

        settings.Normalize();

        Assert.NotNull(settings.KeyboardShortcut);
        Assert.False(settings.KeyboardShortcut.IsConfigured);
    }

    [Fact]
    public void AppSettings_KeepsHpServiceSuppressionOptIn()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;

        settings.Normalize();

        Assert.False(settings.SuppressHpAppServices);
    }

    [Fact]
    public void AppSettings_EnablesWindowsStartupByDefault()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;

        settings.Normalize();

        Assert.True(settings.StartWithWindows);
    }

    [Fact]
    public void StartupController_TogglesIsolatedRegistryEntry()
    {
        var root = $@"Software\VictusModeSwitch.Tests\{Guid.NewGuid():N}";
        var runKeyPath = $@"{root}\Run";
        var approvedKeyPath = $@"{root}\StartupApproved";
        var executablePath = Path.Combine(Path.GetTempPath(), "Victus Mode Switch Test.exe");
        var controller = new StartupController(
            executablePath,
            runKeyPath,
            approvedKeyPath,
            "StartupTest");

        try
        {
            controller.SetEnabled(true);
            Assert.True(controller.IsEnabled());

            using (var approvedKey = Registry.CurrentUser.CreateSubKey(approvedKeyPath))
            {
                approvedKey.SetValue(
                    "StartupTest",
                    new byte[] { 3, 0, 0, 0 },
                    RegistryValueKind.Binary);
            }

            Assert.False(controller.IsEnabled());
            controller.SetEnabled(true);
            Assert.True(controller.IsEnabled());

            controller.SetEnabled(false);
            Assert.False(controller.IsEnabled());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void StartupController_RecognizesOnlyCurrentExecutable()
    {
        const string executable = @"C:\Apps\Victus Mode Switch\VictusModeSwitch.exe";

        Assert.Equal($"\"{executable}\"", StartupController.BuildCommand(executable));
        Assert.True(StartupController.CommandTargetsExecutable($"\"{executable}\"", executable));
        Assert.False(StartupController.CommandTargetsExecutable(
            "\"C:\\Old\\VictusModeSwitch.exe\"",
            executable));
    }

    [Fact]
    public void HpServiceSuppressor_FiltersUnknownNamesAndUsesStableOrder()
    {
        var services = HpServiceSuppressor.FilterTargetServices(new[]
        {
            "NotAnHpService",
            "HPOmenCap",
            "hpnetworkcap",
            "HPOmenCap"
        });

        Assert.Equal(new[] { "HPNetworkCap", "HPOmenCap" }, services);
    }

    [Fact]
    public void BiosBroker_RetriesTransientCommunicationFailuresOnly()
    {
        Assert.True(ElevatedBiosBroker.IsTransientHardwareFailure(new TimeoutException()));
        Assert.True(ElevatedBiosBroker.IsTransientHardwareFailure(new IOException()));
        Assert.True(ElevatedBiosBroker.IsTransientHardwareFailure(
            new InvalidOperationException("The BIOS task returned an empty response.")));
        Assert.False(ElevatedBiosBroker.IsTransientHardwareFailure(
            new InvalidOperationException("Unsupported system board 'FFFF'.")));
        Assert.False(ElevatedBiosBroker.IsTransientHardwareFailure(
            new InvalidOperationException("Unsupported HP Thermal Policy V1.")));
        Assert.False(ElevatedBiosBroker.IsTransientHardwareFailure(new UnauthorizedAccessException()));
    }

    [Fact]
    public void HotkeyBinding_RequiresModifierAndFormatsCombination()
    {
        var withoutModifier = new HotkeyBinding { VirtualKey = (int)Keys.K }.Normalize();
        var binding = new HotkeyBinding
        {
            VirtualKey = (int)Keys.K,
            Modifiers = GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt
        }.Normalize();

        Assert.False(withoutModifier.IsConfigured);
        Assert.True(binding.IsConfigured);
        Assert.Equal("Ctrl + Alt + K", binding.ToDisplayString());
    }

    [Fact]
    public void HotkeyBinding_RejectsModifierOnlyKey()
    {
        var binding = new HotkeyBinding
        {
            VirtualKey = (int)Keys.LControlKey,
            Modifiers = GlobalHotkeyModifiers.Control
        }.Normalize();

        Assert.False(binding.IsConfigured);
    }

    [Fact]
    public void GlobalHotkeyController_RejectsConflictAndRestoresPreviousBinding()
    {
        var occupied = new HotkeyBinding
        {
            VirtualKey = (int)Keys.F24,
            Modifiers = GlobalHotkeyModifiers.Control |
                        GlobalHotkeyModifiers.Alt |
                        GlobalHotkeyModifiers.Shift
        };
        var fallback = occupied with { VirtualKey = (int)Keys.F23 };
        using var owner = new GlobalHotkeyController(writeLog: false);
        using var subject = new GlobalHotkeyController(writeLog: false);

        Assert.True(owner.Apply(occupied).Success);
        Assert.True(subject.Apply(fallback).Success);
        var result = subject.Apply(occupied);

        Assert.False(result.Success);
        Assert.Equal(HotkeyRegistrationResult.AlreadyRegisteredError, result.ErrorCode);
        Assert.Equal(fallback, subject.Binding);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(144, 60)]
    public void NvidiaEcoLimit_PreservesLowerExistingLimit(int current, int expected)
    {
        Assert.Equal(
            (uint)expected,
            NvidiaFrameRateController.GetEcoLimit((uint)current, maximumFrameRate: 60));
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(60, 60)]
    [InlineData(144, 60)]
    public void DisplayEcoLimit_DoesNotRaiseLowerRefreshRate(int current, int expected)
    {
        Assert.Equal(
            (uint)expected,
            DisplayRefreshRateController.GetLimitedFrequency((uint)current, maximum: 60));
    }

    private sealed class InstallerFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"VictusModeSwitch.Tests.{Guid.NewGuid():N}");

        public InstallerFixture()
        {
            Directory.CreateDirectory(_directory);
            InstallerPath = Path.Combine(_directory, UpdateService.InstallerAssetName);
            File.WriteAllText(InstallerPath, "test installer payload");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(InstallerPath)));
            File.WriteAllText(
                InstallerPath + ".sha256",
                $"{hash.ToLowerInvariant()}  {UpdateService.InstallerAssetName}\n");
        }

        public string InstallerPath { get; }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
