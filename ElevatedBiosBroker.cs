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
    private static readonly JsonSerializerOptions JsonOptions = new();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public async Task ApplyAsync(AppMode mode, bool maxFanEnabled)
    {
        AppPaths.EnsureCreated();
        var request = new BiosTaskRequest(
            Guid.NewGuid(),
            mode.ToString(),
            maxFanEnabled,
            DateTimeOffset.UtcNow);

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

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Windows не запустила BIOS-задачу '{TaskName}', код {process.ExitCode}.");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token);
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
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));

            var resultJson = await ReadBoundedLineAsync(reader, timeout.Token);
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

            if (result.MaxFanEnabled != maxFanEnabled)
            {
                throw new InvalidOperationException("BIOS не подтвердил запрошенное состояние Max Fan.");
            }

            return;
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("BIOS-задача не вернула ответ за 8 секунд.", exception);
        }
    }

    private static string GetPipeName()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Не удалось определить пользователя BIOS-помощника.");
        return $"VictusModeSwitch.Bios.{sid.Replace('-', '.')}";
    }

    private static async Task<string> ReadBoundedLineAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[1];
        while (result.Length <= MaximumResultLength)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
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
    string Mode,
    bool MaxFanEnabled,
    DateTimeOffset CreatedAt);
internal sealed record BiosTaskResult(Guid Id, bool Success, string Message, bool MaxFanEnabled);
