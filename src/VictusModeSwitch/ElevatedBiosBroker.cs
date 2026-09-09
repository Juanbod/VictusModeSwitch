using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace VictusModeSwitch;

internal sealed class ElevatedBiosBroker
{
    public const string TaskName = "Victus Mode Switch BIOS";
    private const int MaximumResultLength = 4096;
    private static readonly SemaphoreSlim TaskGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan[] HardwareRetryDelays =
    {
        TimeSpan.FromMilliseconds(350),
        TimeSpan.FromMilliseconds(900)
    };

    public async Task ApplyAsync(AppMode mode, bool maxFanEnabled)
    {
        AppPaths.EnsureCreated();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var request = new BiosTaskRequest(
                    Guid.NewGuid(),
                    ElevatedBrokerOperation.ApplyHardware.ToString(),
                    mode.ToString(),
                    maxFanEnabled,
                    Array.Empty<string>(),
                    DateTimeOffset.UtcNow);

                var result = await RunAsync(request).ConfigureAwait(false);
                if (result.MaxFanEnabled != maxFanEnabled)
                {
                    throw new InvalidOperationException("BIOS не подтвердил запрошенное состояние Max Fan.");
                }

                return;
            }
            catch (Exception exception) when (
                attempt < HardwareRetryDelays.Length && IsTransientHardwareFailure(exception))
            {
                var delay = HardwareRetryDelays[attempt];
                Log.Warning(
                    $"BIOS временно недоступен, повтор {attempt + 2}/{HardwareRetryDelays.Length + 1} " +
                    $"через {delay.TotalMilliseconds:0} мс: {exception.Message}");
                await Task.Delay(delay).ConfigureAwait(false);
            }
        }
    }

    internal static bool IsTransientHardwareFailure(Exception exception)
    {
        if (exception is TimeoutException or IOException)
        {
            return true;
        }

        if (exception is not InvalidOperationException)
        {
            return false;
        }

        return !exception.Message.Contains("Unsupported system board", StringComparison.OrdinalIgnoreCase) &&
               !exception.Message.Contains("Thermal Policy", StringComparison.OrdinalIgnoreCase);
    }

    public async Task SetHpServicesAsync(bool stop, IEnumerable<string> services)
    {
        var filtered = HpServiceSuppressor.FilterTargetServices(services);
        if (filtered.Count == 0)
        {
            return;
        }

        var operation = stop
            ? ElevatedBrokerOperation.StopHpServices
            : ElevatedBrokerOperation.StartHpServices;
        var request = new BiosTaskRequest(
            Guid.NewGuid(),
            operation.ToString(),
            string.Empty,
            false,
            filtered.ToArray(),
            DateTimeOffset.UtcNow);
        await RunAsync(request).ConfigureAwait(false);
    }

    private static async Task<BiosTaskResult> RunAsync(BiosTaskRequest request)
    {
        await TaskGate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var processLock = await AcquireProcessLockAsync().ConfigureAwait(false);
            return await RunCoreAsync(request).ConfigureAwait(false);
        }
        finally
        {
            TaskGate.Release();
        }
    }

    private static async Task<BiosTaskResult> RunCoreAsync(BiosTaskRequest request)
    {
        using var pipe = new NamedPipeServerStream(
            GetPipeName(),
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            Arguments = $"/Run /TN \"{TaskName}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("Не удалось запустить Планировщик задач Windows.");

        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Windows не запустила BIOS-задачу '{TaskName}', код {process.ExitCode}.");
        }

        var timeoutSeconds = string.Equals(
            request.Operation,
            ElevatedBrokerOperation.ApplyHardware.ToString(),
            StringComparison.Ordinal)
            ? 8
            : 30;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            using var writer = new StreamWriter(pipe, Utf8NoBom, bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(
                pipe,
                Utf8NoBom,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions)).ConfigureAwait(false);

            var resultJson = await ReadBoundedLineAsync(reader, timeout.Token).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<BiosTaskResult>(resultJson, JsonOptions)
                ?? throw new InvalidOperationException("BIOS-задача вернула пустой ответ.");
            if (result.Id != request.Id)
            {
                throw new InvalidOperationException("BIOS-задача вернула ответ для другого запроса.");
            }

            if (!result.Success)
            {
                throw new InvalidOperationException(result.Message);
            }

            return result;
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Привилегированная задача не вернула ответ за {timeoutSeconds} секунд.",
                exception);
        }
    }

    private static string GetPipeName()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Не удалось определить пользователя BIOS-помощника.");
        return $"VictusModeSwitch.Bios.{sid.Replace('-', '.')}";
    }

    private static async Task<FileStream> AcquireProcessLockAsync()
    {
        AppPaths.EnsureCreated();
        var deadline = DateTime.UtcNow.AddSeconds(35);
        while (true)
        {
            try
            {
                return new FileStream(
                    AppPaths.BrokerLockFile,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new TimeoutException(
                    "Другая привилегированная команда не завершилась за 35 секунд.",
                    exception);
            }
        }
    }

    private static async Task<string> ReadBoundedLineAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[1];
        while (result.Length <= MaximumResultLength)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            if (buffer[0] == '\n')
            {
                return result.ToString().TrimEnd('\r');
            }

            result.Append(buffer[0]);
        }

        if (result.Length > MaximumResultLength)
        {
            throw new InvalidOperationException("Ответ BIOS-задачи превышает допустимый размер.");
        }

        throw new EndOfStreamException("BIOS-задача закрыла канал без полного ответа.");
    }
}

internal sealed record BiosTaskRequest(
    Guid Id,
    string Operation,
    string Mode,
    bool MaxFanEnabled,
    string[] Services,
    DateTimeOffset CreatedAt);
internal sealed record BiosTaskResult(
    Guid Id,
    bool Success,
    string Message,
    bool MaxFanEnabled,
    string[]? AffectedServices);

internal enum ElevatedBrokerOperation
{
    ApplyHardware,
    StopHpServices,
    StartHpServices
}
