using System.ServiceProcess;
using System.Text.Json;

namespace VictusModeSwitch;

internal sealed class HpServiceSuppressor : IDisposable
{
    private static readonly string[] TargetServices =
    {
        "HPAppHelperCap",
        "HPDiagsCap",
        "HPNetworkCap",
        "HPOmenCap",
        "HPSysInfoCap"
    };

    private readonly ElevatedBiosBroker _broker = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly HashSet<string> _servicesToRestore = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private volatile bool _enabled;
    private bool _disposed;

    public HpServiceSuppressor()
    {
        foreach (var service in LoadRecoveryServices())
        {
            _servicesToRestore.Add(service);
        }
    }

    public bool Enabled => _enabled;

    internal static IReadOnlyList<string> FilterTargetServices(IEnumerable<string> serviceNames)
    {
        var requested = new HashSet<string>(serviceNames, StringComparer.OrdinalIgnoreCase);
        return TargetServices.Where(requested.Contains).ToArray();
    }

    public async Task<HpServiceSuppressionResult> SetEnabledAsync(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (enabled)
            {
                if (!_enabled)
                {
                    _enabled = true;
                    _monitorCancellation = new CancellationTokenSource();
                    _monitorTask = MonitorAsync(_monitorCancellation.Token);
                }

                return await SuppressRunningServicesAsync().ConfigureAwait(false);
            }

            _enabled = false;
            var cancellation = _monitorCancellation;
            var monitor = _monitorTask;
            _monitorCancellation = null;
            _monitorTask = null;
            cancellation?.Cancel();
            if (monitor is not null)
            {
                try
                {
                    await monitor.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            cancellation?.Dispose();
            return await RestoreServicesAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            SetEnabledAsync(false).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Log.Error("Не удалось восстановить службы HP при завершении", exception);
        }

        _disposed = true;
        _lifecycleGate.Dispose();
        _operationGate.Dispose();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var result = await SuppressRunningServicesAsync().ConfigureAwait(false);
                if (!result.Success)
                {
                    Log.Warning(result.Message);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Log.Error("Проверка фоновых служб HP завершилась ошибкой", exception);
            }
        }
    }

    private async Task<HpServiceSuppressionResult> SuppressRunningServicesAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_enabled)
            {
                return HpServiceSuppressionResult.Succeeded();
            }

            var running = FindRunningTargetServices();
            if (running.Count == 0)
            {
                return HpServiceSuppressionResult.Succeeded();
            }

            foreach (var service in running)
            {
                _servicesToRestore.Add(service);
            }

            SaveRecoveryServices();
            await _broker.SetHpServicesAsync(stop: true, running).ConfigureAwait(false);
            Log.Info($"Приостановлены службы HP: {string.Join(", ", running)}");
            return HpServiceSuppressionResult.Succeeded(running);
        }
        catch (Exception exception)
        {
            Log.Error("Не удалось приостановить фоновые службы HP", exception);
            return HpServiceSuppressionResult.Failed(exception.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<HpServiceSuppressionResult> RestoreServicesAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var services = FilterTargetServices(_servicesToRestore);
            if (services.Count == 0)
            {
                DeleteRecoveryFile();
                return HpServiceSuppressionResult.Succeeded();
            }

            await _broker.SetHpServicesAsync(stop: false, services).ConfigureAwait(false);
            _servicesToRestore.Clear();
            DeleteRecoveryFile();
            Log.Info($"Восстановлены службы HP: {string.Join(", ", services)}");
            return HpServiceSuppressionResult.Succeeded(services);
        }
        catch (Exception exception)
        {
            Log.Error("Не удалось восстановить фоновые службы HP", exception);
            return HpServiceSuppressionResult.Failed(exception.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static IReadOnlyList<string> FindRunningTargetServices()
    {
        var running = new List<string>();
        foreach (var name in TargetServices)
        {
            using var service = new ServiceController(name);
            try
            {
                if (service.Status == ServiceControllerStatus.Running)
                {
                    running.Add(name);
                }
            }
            catch (InvalidOperationException)
            {
                // An HP package may omit some services from the fixed allowlist.
            }
        }

        return running;
    }

    private static IReadOnlyList<string> LoadRecoveryServices()
    {
        try
        {
            if (!File.Exists(AppPaths.HpServiceSessionFile))
            {
                return Array.Empty<string>();
            }

            var saved = JsonSerializer.Deserialize<string[]>(
                File.ReadAllText(AppPaths.HpServiceSessionFile));
            return FilterTargetServices(saved ?? Array.Empty<string>());
        }
        catch (Exception exception)
        {
            Log.Error("Не удалось прочитать файл восстановления служб HP", exception);
            return Array.Empty<string>();
        }
    }

    private void SaveRecoveryServices()
    {
        AppPaths.EnsureCreated();
        var temporary = AppPaths.HpServiceSessionFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(FilterTargetServices(_servicesToRestore)));
        File.Move(temporary, AppPaths.HpServiceSessionFile, true);
    }

    private static void DeleteRecoveryFile()
    {
        File.Delete(AppPaths.HpServiceSessionFile);
        File.Delete(AppPaths.HpServiceSessionFile + ".tmp");
    }
}

internal sealed record HpServiceSuppressionResult(
    bool Success,
    string Message,
    IReadOnlyList<string> Services)
{
    public static HpServiceSuppressionResult Succeeded(IReadOnlyList<string>? services = null) =>
        new(true, string.Empty, services ?? Array.Empty<string>());

    public static HpServiceSuppressionResult Failed(string message) =>
        new(false, message, Array.Empty<string>());
}
