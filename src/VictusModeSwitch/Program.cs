using System.Text.Json;
using System.Text.Json.Serialization;

namespace VictusModeSwitch;

internal static class Program
{
    public const string MutexName = "Local\\VictusModeSwitch.8A4F";
    public const string OpenSettingsEventName = "Local\\VictusModeSwitch.OpenSettings.8A4F";

    [STAThread]
    private static void Main(string[] args)
    {
        AppPaths.EnsureCreated();
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            Log.Error("Необработанная ошибка", eventArgs.ExceptionObject as Exception);

        if (args.Length > 0 &&
            string.Equals(args[0], "--settings", StringComparison.OrdinalIgnoreCase) &&
            TrySignalRunningSettingsWindow())
        {
            return;
        }

        if (args.Length > 0)
        {
            RunCommand(args);
            return;
        }

        using var mutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }

    private static bool TrySignalRunningSettingsWindow()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(OpenSettingsEventName);
            signal.Set();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    private static void RunCommand(IReadOnlyList<string> args)
    {
        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "--diagnose":
                    var seconds = args.Count > 1 && int.TryParse(args[1], out var parsed) ? parsed : 0;
                    Diagnose(Math.Clamp(seconds, 0, 120));
                    break;
                case "--settings":
                    ApplicationConfiguration.Initialize();
                    var store = new AppSettingsStore();
                    Application.Run(new SettingsForm(store, new ModeController(store)));
                    break;
                case "--toast-test" when args.Count > 1:
                    ShowToast(args[1]);
                    break;
                case "--set" when args.Count > 1:
                    SetMode(args[1]);
                    break;
                case "--max-fan" when args.Count > 1:
                    SetMaxFan(args[1]);
                    break;
                case "--windows-tuning" when args.Count > 1:
                    SetWindowsTuning(args[1]);
                    break;
                case "--export-status" when args.Count > 1:
                    ExportStatus(args[1]);
                    break;
                case "--version":
                    Console.WriteLine(AppVersion.Display);
                    break;
                case "--install-update" when args.Count > 2 && int.TryParse(args[2], out var parentProcessId):
                    UpdateService.InstallAfterParentExit(args[1], parentProcessId);
                    break;
                default:
                    Log.Warning($"Неизвестная команда: {string.Join(' ', args)}");
                    Environment.ExitCode = 2;
                    break;
            }
        }
        catch (Exception exception)
        {
            Log.Error("Команда завершилась с ошибкой", exception);
            Environment.ExitCode = 1;
        }
    }

    private static void Diagnose(int seconds)
    {
        var model = HardwareInfo.GetComputerModel();
        var board = HardwareInfo.GetBoardProduct();
        Log.Info($"DIAG: Model='{model}', Board='{board}'");
        HardwareInfo.EnsureSupportedBoard();

        try
        {
            using var bios = new HpBios();
            var design = bios.ReadSystemDesign();
            var prefix = Convert.ToHexString(design.Data.Take(12).ToArray());
            Log.Info($"DIAG: BIOS code={design.ReturnCode}, ThermalPolicy={design.Data.ElementAtOrDefault(3)}, Data[0..12]={prefix}");
        }
        catch (System.Management.ManagementException exception)
            when (exception.ErrorCode == System.Management.ManagementStatus.AccessDenied)
        {
            Log.Info("DIAG: прямой BIOS WMI ожидаемо требует повышенных прав");
        }

        if (seconds <= 0)
        {
            return;
        }

        var presses = 0;
        using var listener = new OmenKeyListener(diagnosticLogging: true);
        listener.EventObserved += (eventId, eventData) =>
            Log.Info($"DIAG: HP event EventID={eventId}, EventData={eventData}");
        listener.OmenKeyPressed += () => Interlocked.Increment(ref presses);
        listener.Start();
        Log.Info($"DIAG: ожидание событий {seconds} с");
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        Log.Info($"DIAG: ожидание завершено, нажатий OMEN key={presses}");
        if (presses == 0)
        {
            Environment.ExitCode = 3;
        }
    }

    private static void SetMode(string value)
    {
        var mode = value.ToLowerInvariant() switch
        {
            "eco" => AppMode.Eco,
            "standard" or "default" => AppMode.Standard,
            "performance" => AppMode.Performance,
            _ => throw new ArgumentException($"Неизвестный режим '{value}'.")
        };

        var controller = new ModeController(new AppSettingsStore());
        var result = controller.ApplyAsync(mode).GetAwaiter().GetResult();
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join("; ", result.Warnings));
        }
    }

    private static void SetMaxFan(string value)
    {
        var enabled = ParseToggle(value, "Max Fan");
        var controller = new ModeController(new AppSettingsStore());
        var result = controller.SetMaxFanAsync(enabled).GetAwaiter().GetResult();
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join("; ", result.Warnings));
        }
    }

    private static void SetWindowsTuning(string value)
    {
        var enabled = ParseToggle(value, "Windows tuning");
        var store = new AppSettingsStore();
        store.Settings.PowerTuning.Enabled = enabled;
        store.Save();
        var result = new ModeController(store).ApplyAsync(store.Settings.CurrentMode, reapply: true)
            .GetAwaiter().GetResult();
        if (!result.Success || result.Warnings.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", result.Warnings));
        }
    }

    private static bool ParseToggle(string value, string name) => value.ToLowerInvariant() switch
    {
        "on" or "1" or "true" => true,
        "off" or "0" or "false" => false,
        _ => throw new ArgumentException($"Неизвестное состояние {name} '{value}'.")
    };

    private static void ShowToast(string value)
    {
        ApplicationConfiguration.Initialize();
        var store = new AppSettingsStore();
        var localizer = new Localizer(store.Settings.Language);
        var toast = value.ToLowerInvariant() switch
        {
            "eco" => ModeToastForm.ForMode(AppMode.Eco, Array.Empty<string>(), localizer),
            "standard" => ModeToastForm.ForMode(AppMode.Standard, Array.Empty<string>(), localizer),
            "performance" => ModeToastForm.ForMode(AppMode.Performance, Array.Empty<string>(), localizer),
            "fan-on" => ModeToastForm.ForMaxFan(true, localizer),
            "fan-off" => ModeToastForm.ForMaxFan(false, localizer),
            _ => throw new ArgumentException($"Неизвестная тестовая плашка '{value}'.")
        };

        Application.Run(toast);
    }

    private static void ExportStatus(string destination)
    {
        var fullPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Не удалось определить каталог файла состояния."));
        var store = new AppSettingsStore();
        var payload = new
        {
            Version = AppVersion.Display,
            CapturedAt = DateTimeOffset.Now,
            Model = HardwareInfo.GetComputerModel(),
            Board = HardwareInfo.GetBoardProduct(),
            store.Settings.CurrentMode,
            store.Settings.MaxFanEnabled,
            PowerTuning = store.Settings.PowerTuning.Enabled
        };
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(fullPath, JsonSerializer.Serialize(payload, options));
    }
}
