using System.Text.Json;
using System.Text.Json.Serialization;

namespace VictusModeSwitch;

internal sealed class AppSettings
{
    public AppMode CurrentMode { get; set; } = AppMode.Standard;
    public bool MaxFanEnabled { get; set; }
    public string Language { get; set; } = "system";
    public bool ShowNotifications { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public bool SuppressHpAppServices { get; set; }
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public HotkeyBinding KeyboardShortcut { get; set; } = new();
    public PowerTuningSettings PowerTuning { get; set; } = new();
    public WindowsPowerBackup WindowsPower { get; set; } = new();
    public EcoBehaviorSettings EcoBehavior { get; set; } = new();
    public EcoBehaviorBackup EcoBehaviorBackup { get; set; } = new();

    // Reads the v1.x property without writing it back to the new settings file.
    [JsonPropertyName("Experimental")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PowerTuningSettings? LegacyPowerTuning
    {
        get => null;
        set
        {
            if (value is not null)
            {
                PowerTuning = value;
            }
        }
    }

    public void Normalize()
    {
        Language = Language?.ToLowerInvariant() switch
        {
            "ru" => "ru",
            "uk" => "uk",
            "en" => "en",
            _ => "system"
        };
        KeyboardShortcut = (KeyboardShortcut ?? new HotkeyBinding()).Normalize();
        PowerTuning ??= new PowerTuningSettings();
        PowerTuning.EcoAcMaximumProcessor = Math.Clamp(PowerTuning.EcoAcMaximumProcessor, 5, 100);
        PowerTuning.EcoBatteryMaximumProcessor = Math.Clamp(PowerTuning.EcoBatteryMaximumProcessor, 5, 100);
        WindowsPower ??= new WindowsPowerBackup();
        WindowsPower.Normalize();
        EcoBehavior ??= new EcoBehaviorSettings();
        EcoBehaviorBackup ??= new EcoBehaviorBackup();
        EcoBehaviorBackup.Normalize();
    }
}

internal sealed class PowerTuningSettings
{
    public bool Enabled { get; set; }

    // Compatibility with the v1.x JSON field.
    [JsonPropertyName("WindowsPowerTuningEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LegacyEnabled
    {
        get => false;
        set => Enabled = value;
    }

    public int EcoAcMaximumProcessor { get; set; } = 80;
    public int EcoBatteryMaximumProcessor { get; set; } = 65;
}

internal sealed class WindowsPowerBackup
{
    public bool Valid { get; set; }
    public int Revision { get; set; }
    public Guid Scheme { get; set; }
    public PowerValueBackup ProcessorMinimum { get; set; } = new();
    public PowerValueBackup ProcessorMaximum { get; set; } = new();
    public PowerValueBackup ProcessorEpp { get; set; } = new();
    public PowerValueBackup ProcessorEppClass1 { get; set; } = new();
    public PowerValueBackup ProcessorEppClass2 { get; set; } = new();
    public PowerValueBackup CoolingPolicy { get; set; } = new();
    public PowerValueBackup PcieLinkState { get; set; } = new();
    public PowerValueBackup WirelessPowerSaving { get; set; } = new();
    public PowerValueBackup UsbSelectiveSuspend { get; set; } = new();
    public PowerModeBackup PowerMode { get; set; } = new();

    public void Normalize()
    {
        ProcessorMinimum ??= new PowerValueBackup();
        ProcessorMaximum ??= new PowerValueBackup();
        ProcessorEpp ??= new PowerValueBackup();
        ProcessorEppClass1 ??= new PowerValueBackup();
        ProcessorEppClass2 ??= new PowerValueBackup();
        CoolingPolicy ??= new PowerValueBackup();
        PcieLinkState ??= new PowerValueBackup();
        WirelessPowerSaving ??= new PowerValueBackup();
        UsbSelectiveSuspend ??= new PowerValueBackup();
        PowerMode ??= new PowerModeBackup();
    }
}

internal sealed class PowerValueBackup
{
    public bool Valid { get; set; }
    public uint AcValue { get; set; }
    public uint DcValue { get; set; }
}

internal sealed class PowerModeBackup
{
    public bool Valid { get; set; }
    public Guid AcMode { get; set; }
    public Guid DcMode { get; set; }
}

internal sealed class EcoBehaviorSettings
{
    public bool LimitDisplayRefreshRate { get; set; }
    public bool LimitNvidiaFrameRate { get; set; }
    public bool DisableTurboBoost { get; set; }
}

internal sealed class EcoBehaviorBackup
{
    public DisplayRefreshRateBackup DisplayRefreshRate { get; set; } = new();
    public NvidiaFrameRateBackup NvidiaFrameRate { get; set; } = new();
    public TurboBoostBackup TurboBoost { get; set; } = new();

    public void Normalize()
    {
        DisplayRefreshRate ??= new DisplayRefreshRateBackup();
        NvidiaFrameRate ??= new NvidiaFrameRateBackup();
        TurboBoost ??= new TurboBoostBackup();
        TurboBoost.Value ??= new PowerValueBackup();
    }
}

internal sealed class DisplayRefreshRateBackup
{
    public bool Valid { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public uint Frequency { get; set; }
}

internal sealed class NvidiaFrameRateBackup
{
    public bool Valid { get; set; }
    public uint Limit { get; set; }
}

internal sealed class TurboBoostBackup
{
    public bool Valid { get; set; }
    public Guid Scheme { get; set; }
    public PowerValueBackup Value { get; set; } = new();
}

internal sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new AppModeJsonConverter(), new JsonStringEnumConverter() }
    };

    public AppSettings Settings { get; private set; }

    public AppSettingsStore()
    {
        Settings = Load();
    }

    public void Save()
    {
        AppPaths.EnsureCreated();
        Settings.Normalize();
        var temporary = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Settings, JsonOptions));
        File.Move(temporary, AppPaths.SettingsFile, true);
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(AppPaths.SettingsFile),
                    JsonOptions) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception exception)
        {
            Log.Error("Не удалось прочитать настройки, используются безопасные значения", exception);
        }

        var defaults = new AppSettings();
        defaults.Normalize();
        return defaults;
    }
}
