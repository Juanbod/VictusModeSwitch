using System.Management;

namespace VictusModeSwitch;

internal sealed class HpBios : IDisposable
{
    private const uint DefaultCommand = 0x20008;
    private static readonly byte[] Signature = { 0x53, 0x45, 0x43, 0x55 };

    private readonly ManagementScope _scope;
    private readonly ManagementObject _biosMethods;

    public HpBios()
    {
        _scope = new ManagementScope("\\\\.\\root\\wmi");
        _scope.Connect();

        using var searcher = new ManagementObjectSearcher(
            _scope,
            new ObjectQuery("SELECT * FROM hpqBIntM"));
        _biosMethods = searcher.Get().Cast<ManagementObject>().FirstOrDefault()
            ?? throw new InvalidOperationException("HP BIOS WMI interface hpqBIntM не найден.");
    }

    public BiosResult ReadSystemDesign()
    {
        var result = Send(DefaultCommand, 0x28, null, 128);
        EnsureSuccess("чтение System Design", result);
        return result;
    }

    public void EnsureLegacyThermalPolicy()
    {
        var result = ReadSystemDesign();
        if (result.Data.Length < 4)
        {
            throw new InvalidOperationException("BIOS вернул слишком короткий блок System Design.");
        }

        if (result.Data[3] != 0)
        {
            throw new InvalidOperationException(
                $"Запись остановлена: ожидалась Thermal Policy V0, обнаружено значение {result.Data[3]}.");
        }
    }

    public void SetFanMode(AppMode mode)
    {
        var biosMode = mode == AppMode.Performance ? (byte)1 : (byte)0;
        var result = Send(DefaultCommand, 0x1A, new byte[] { 0xFF, biosMode, 0x00, 0x00 }, 4);
        EnsureSuccess($"установка режима {mode}", result);
    }

    public void SetMaxFan(bool enabled)
    {
        var value = enabled ? (byte)1 : (byte)0;
        var result = Send(DefaultCommand, 0x27, new[] { value, (byte)0, (byte)0, (byte)0 }, 4);
        EnsureSuccess($"{(enabled ? "включение" : "отключение")} Max Fan", result);
    }

    public bool GetMaxFan()
    {
        var result = Send(DefaultCommand, 0x26, null, 4);
        EnsureSuccess("чтение Max Fan", result);
        if (result.Data.Length == 0)
        {
            throw new InvalidOperationException("BIOS не вернул состояние Max Fan.");
        }

        return result.Data[0] != 0;
    }

    public BiosResult Send(uint command, uint commandType, byte[]? payload, int outputSize)
    {
        var methodName = $"hpqBIOSInt{outputSize}";
        using var dataClass = new ManagementClass(_scope, new ManagementPath("hpqBDataIn"), null);
        using var inputData = dataClass.CreateInstance()
            ?? throw new InvalidOperationException("Не удалось создать hpqBDataIn.");

        inputData["Sign"] = Signature;
        inputData["Command"] = command;
        inputData["CommandType"] = commandType;
        inputData["Size"] = (uint)(payload?.Length ?? 0);
        if (payload is not null)
        {
            inputData["hpqBData"] = payload;
        }

        using var methodInput = _biosMethods.GetMethodParameters(methodName);
        methodInput["InData"] = inputData;
        using var methodOutput = _biosMethods.InvokeMethod(methodName, methodInput, null)
            ?? throw new InvalidOperationException($"BIOS-метод {methodName} не вернул результат.");
        using var outputData = methodOutput["OutData"] as ManagementBaseObject
            ?? throw new InvalidOperationException($"BIOS-метод {methodName} не вернул OutData.");

        var returnCode = Convert.ToUInt32(outputData["rwReturnCode"]);
        var data = outputSize == 0 ? Array.Empty<byte>() : ToByteArray(outputData["Data"]);
        return new BiosResult(returnCode, data);
    }

    public void Dispose() => _biosMethods.Dispose();

    private static byte[] ToByteArray(object? value)
    {
        if (value is byte[] bytes)
        {
            return bytes;
        }

        if (value is Array array)
        {
            return array.Cast<object>().Select(Convert.ToByte).ToArray();
        }

        return Array.Empty<byte>();
    }

    private static void EnsureSuccess(string operation, BiosResult result)
    {
        if (result.ReturnCode != 0)
        {
            throw new InvalidOperationException(
                $"Ошибка BIOS при операции '{operation}', код {result.ReturnCode}.");
        }
    }
}

internal sealed record BiosResult(uint ReturnCode, byte[] Data);
