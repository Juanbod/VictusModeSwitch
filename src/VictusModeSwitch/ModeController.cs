using System.Diagnostics;

namespace VictusModeSwitch;

internal sealed class ModeController
{
    private readonly AppSettingsStore _store;
    private readonly ElevatedBiosBroker _biosBroker = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ModeController(AppSettingsStore store)
    {
        _store = store;
    }

    public AppMode CurrentMode => _store.Settings.CurrentMode;
    public bool MaxFanEnabled => _store.Settings.MaxFanEnabled;

    public Task<ModeApplyResult> TogglePerformanceAsync() => ApplyAsync(CurrentMode.TogglePerformance());

    public async Task<ModeApplyResult> ApplyAsync(AppMode target, bool reapply = false)
    {
        await _gate.WaitAsync();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var warnings = new List<string>();
            await EnsureSupportedBoardAsync();

            var maxFanEnabled = target == AppMode.Eco && !reapply
                ? false
                : _store.Settings.MaxFanEnabled;
            await _biosBroker.ApplyAsync(target, maxFanEnabled);
            Log.Info($"BIOS: режим {target}, Max Fan {(maxFanEnabled ? "включен" : "выключен")}");

            await ApplyWindowsPowerAsync(target, warnings);
            await ApplyEcoBehaviorAsync(target, warnings);

            _store.Settings.CurrentMode = target;
            _store.Settings.MaxFanEnabled = maxFanEnabled;
            _store.Save();

            Log.Info(
                $"Режим {(reapply ? "повторно применён" : "переключён")}: {target}, " +
                $"{stopwatch.ElapsedMilliseconds} мс");
            return new ModeApplyResult(true, target, warnings);
        }
        catch (Exception exception)
        {
            Log.Error($"Не удалось применить режим {target}", exception);
            return new ModeApplyResult(false, target, new[] { exception.Message });
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<FanApplyResult> ToggleMaxFanAsync() => SetMaxFanAsync(!_store.Settings.MaxFanEnabled);

    public async Task<FanApplyResult> SetMaxFanAsync(bool enabled)
    {
        await _gate.WaitAsync();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await EnsureSupportedBoardAsync();
            await _biosBroker.ApplyAsync(CurrentMode, enabled);
            _store.Settings.MaxFanEnabled = enabled;
            _store.Save();
            Log.Info($"Max Fan {(enabled ? "включен" : "выключен")}, {stopwatch.ElapsedMilliseconds} мс");
            return new FanApplyResult(true, enabled, Array.Empty<string>());
        }
        catch (Exception exception)
        {
            Log.Error("Не удалось переключить Max Fan", exception);
            return new FanApplyResult(false, _store.Settings.MaxFanEnabled, new[] { exception.Message });
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ApplyWindowsPowerAsync(AppMode target, List<string> warnings)
    {
        var shouldApply = _store.Settings.PowerTuning.Enabled && target != AppMode.Standard;
        if (!shouldApply)
        {
            if (!_store.Settings.WindowsPower.Valid)
            {
                return;
            }

            var restored = await RunOptionalAsync(
                "восстановить параметры питания Windows",
                () => WindowsPowerTuningController.Restore(_store.Settings.WindowsPower));
            if (restored.Success)
            {
                _store.Settings.WindowsPower = new WindowsPowerBackup();
                _store.Save();
            }
            else
            {
                warnings.Add(restored.Warning);
            }

            return;
        }

        if (_store.Settings.WindowsPower.Valid &&
            !WindowsPowerTuningController.UsesCurrentScheme(_store.Settings.WindowsPower))
        {
            var restored = await RunOptionalAsync(
                "восстановить прежнюю схему питания Windows",
                () => WindowsPowerTuningController.Restore(_store.Settings.WindowsPower));
            if (!restored.Success)
            {
                warnings.Add(restored.Warning);
                return;
            }

            _store.Settings.WindowsPower = new WindowsPowerBackup();
            _store.Save();
        }

        if (!_store.Settings.WindowsPower.Valid)
        {
            var capture = await CaptureAsync(
                "сохранить параметры питания Windows",
                WindowsPowerTuningController.Capture);
            if (!capture.Success || capture.Value is null)
            {
                warnings.Add(capture.Warning);
                return;
            }

            _store.Settings.WindowsPower = capture.Value;
            _store.Save();
        }

        var applied = await RunOptionalAsync(
            $"применить параметры питания Windows для {target}",
            () => WindowsPowerTuningController.Apply(
                target,
                _store.Settings.WindowsPower,
                _store.Settings.PowerTuning));
        if (!applied.Success)
        {
            warnings.Add(applied.Warning);
        }
    }

    private async Task ApplyEcoBehaviorAsync(AppMode target, List<string> warnings)
    {
        var eco = target == AppMode.Eco;
        await ApplyDisplayRefreshRateAsync(
            eco && _store.Settings.EcoBehavior.LimitDisplayRefreshRate,
            warnings);
        await ApplyNvidiaFrameRateAsync(
            eco && _store.Settings.EcoBehavior.LimitNvidiaFrameRate,
            warnings);
        await ApplyTurboBoostAsync(
            eco && _store.Settings.EcoBehavior.DisableTurboBoost,
            warnings);
    }

    private async Task ApplyDisplayRefreshRateAsync(bool enabled, List<string> warnings)
    {
        var backup = _store.Settings.EcoBehaviorBackup.DisplayRefreshRate;
        if (enabled)
        {
            if (!backup.Valid)
            {
                var capture = await CaptureAsync(
                    "сохранить частоту встроенного дисплея",
                    DisplayRefreshRateController.Capture);
                if (!capture.Success || capture.Value is null)
                {
                    warnings.Add(capture.Warning);
                    return;
                }

                backup = capture.Value;
                _store.Settings.EcoBehaviorBackup.DisplayRefreshRate = backup;
                _store.Save();
            }

            var applied = await RunOptionalAsync(
                "ограничить встроенный дисплей до 60 Гц",
                () => DisplayRefreshRateController.ApplyLimit(backup));
            if (!applied.Success)
            {
                warnings.Add(applied.Warning);
            }

            return;
        }

        if (!backup.Valid)
        {
            return;
        }

        var restored = await RunOptionalAsync(
            "восстановить частоту встроенного дисплея",
            () => DisplayRefreshRateController.Restore(backup));
        if (restored.Success)
        {
            _store.Settings.EcoBehaviorBackup.DisplayRefreshRate = new DisplayRefreshRateBackup();
            _store.Save();
        }
        else
        {
            warnings.Add(restored.Warning);
        }
    }

    private async Task ApplyNvidiaFrameRateAsync(bool enabled, List<string> warnings)
    {
        var backup = _store.Settings.EcoBehaviorBackup.NvidiaFrameRate;
        if (enabled)
        {
            if (!backup.Valid)
            {
                var capture = await CaptureAsync(
                    "сохранить ограничение FPS NVIDIA",
                    NvidiaFrameRateController.Capture);
                if (!capture.Success || capture.Value is null)
                {
                    warnings.Add(capture.Warning);
                    return;
                }

                backup = capture.Value;
                _store.Settings.EcoBehaviorBackup.NvidiaFrameRate = backup;
                _store.Save();
            }

            var applied = await RunOptionalAsync(
                "ограничить NVIDIA до 60 FPS",
                () => NvidiaFrameRateController.ApplyLimit(backup));
            if (!applied.Success)
            {
                warnings.Add(applied.Warning);
            }

            return;
        }

        if (!backup.Valid)
        {
            return;
        }

        var restored = await RunOptionalAsync(
            "восстановить ограничение FPS NVIDIA",
            () => NvidiaFrameRateController.Restore(backup));
        if (restored.Success)
        {
            _store.Settings.EcoBehaviorBackup.NvidiaFrameRate = new NvidiaFrameRateBackup();
            _store.Save();
        }
        else
        {
            warnings.Add(restored.Warning);
        }
    }

    private async Task ApplyTurboBoostAsync(bool enabled, List<string> warnings)
    {
        var backup = _store.Settings.EcoBehaviorBackup.TurboBoost;
        if (enabled && backup.Valid)
        {
            var usesCurrentScheme = false;
            var checkedScheme = await RunOptionalAsync(
                "проверить схему питания для Turbo Boost",
                () => usesCurrentScheme = WindowsPowerTuningController.UsesCurrentScheme(backup));
            if (!checkedScheme.Success)
            {
                warnings.Add(checkedScheme.Warning);
                return;
            }

            if (!usesCurrentScheme)
            {
                var restoredOldScheme = await RunOptionalAsync(
                    "восстановить Turbo Boost в прежней схеме питания",
                    () => WindowsPowerTuningController.RestoreTurboBoost(backup));
                if (!restoredOldScheme.Success)
                {
                    warnings.Add(restoredOldScheme.Warning);
                    return;
                }

                backup = new TurboBoostBackup();
                _store.Settings.EcoBehaviorBackup.TurboBoost = backup;
                _store.Save();
            }
        }

        if (enabled)
        {
            if (!backup.Valid)
            {
                var capture = await CaptureAsync(
                    "сохранить настройку Turbo Boost",
                    WindowsPowerTuningController.CaptureTurboBoost);
                if (!capture.Success || capture.Value is null)
                {
                    warnings.Add(capture.Warning);
                    return;
                }

                backup = capture.Value;
                _store.Settings.EcoBehaviorBackup.TurboBoost = backup;
                _store.Save();
            }

            var applied = await RunOptionalAsync(
                "отключить Turbo Boost",
                () => WindowsPowerTuningController.DisableTurboBoost(backup));
            if (!applied.Success)
            {
                warnings.Add(applied.Warning);
            }

            return;
        }

        if (!backup.Valid)
        {
            return;
        }

        var restored = await RunOptionalAsync(
            "восстановить Turbo Boost",
            () => WindowsPowerTuningController.RestoreTurboBoost(backup));
        if (restored.Success)
        {
            _store.Settings.EcoBehaviorBackup.TurboBoost = new TurboBoostBackup();
            _store.Save();
        }
        else
        {
            warnings.Add(restored.Warning);
        }
    }

    private static async Task EnsureSupportedBoardAsync()
    {
        try
        {
            await Task.Run(HardwareInfo.EnsureSupportedBoard);
        }
        catch (InvalidCastException)
        {
            Log.Warning("Повторная проверка системной платы после временной ошибки WMI");
            await Task.Delay(50);
            await Task.Run(HardwareInfo.EnsureSupportedBoard);
        }
    }

    private static Task<OptionalValue<T>> CaptureAsync<T>(string name, Func<T> capture)
        where T : class => Task.Run(() =>
    {
        try
        {
            var value = capture();
            Log.Info($"Доп. настройка: успешно - {name}");
            return new OptionalValue<T>(true, value, string.Empty);
        }
        catch (Exception exception)
        {
            Log.Error($"Доп. настройка: не удалось {name}", exception);
            return new OptionalValue<T>(false, null, $"Не удалось {name}");
        }
    });

    private static Task<OptionalResult> RunOptionalAsync(string name, Action action) => Task.Run(() =>
    {
        try
        {
            action();
            Log.Info($"Доп. настройка: успешно - {name}");
            return new OptionalResult(true, string.Empty);
        }
        catch (Exception exception)
        {
            Log.Error($"Доп. настройка: не удалось {name}", exception);
            return new OptionalResult(false, $"Не удалось {name}");
        }
    });
}

internal sealed record ModeApplyResult(bool Success, AppMode Mode, IReadOnlyCollection<string> Warnings);
internal sealed record FanApplyResult(bool Success, bool Enabled, IReadOnlyCollection<string> Warnings);
internal sealed record OptionalResult(bool Success, string Warning);
internal sealed record OptionalValue<T>(bool Success, T? Value, string Warning) where T : class;
