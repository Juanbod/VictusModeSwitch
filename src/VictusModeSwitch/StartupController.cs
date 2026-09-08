using Microsoft.Win32;

namespace VictusModeSwitch;

internal sealed class StartupController
{
    internal const string DefaultValueName = "VictusModeSwitch";
    internal const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string DefaultStartupApprovedKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly string _executablePath;
    private readonly string _runKeyPath;
    private readonly string _startupApprovedKeyPath;
    private readonly string _valueName;

    public StartupController(
        string? executablePath = null,
        string runKeyPath = DefaultRunKeyPath,
        string startupApprovedKeyPath = DefaultStartupApprovedKeyPath,
        string valueName = DefaultValueName)
    {
        var processPath = executablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("The application executable path is unavailable.");
        _executablePath = Path.GetFullPath(processPath);
        _runKeyPath = runKeyPath;
        _startupApprovedKeyPath = startupApprovedKeyPath;
        _valueName = valueName;
    }

    public bool IsEnabled()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: false);
        var command = runKey?.GetValue(
            _valueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (!CommandTargetsExecutable(command, _executablePath))
        {
            return false;
        }

        using var approvalKey = Registry.CurrentUser.OpenSubKey(
            _startupApprovedKeyPath,
            writable: false);
        return !IsExplicitlyDisabled(approvalKey?.GetValue(_valueName) as byte[]);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true)
                ?? throw new InvalidOperationException("Windows could not open the startup registry key.");
            runKey.SetValue(
                _valueName,
                BuildCommand(_executablePath),
                RegistryValueKind.String);
        }
        else
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
            runKey?.DeleteValue(_valueName, throwOnMissingValue: false);
        }

        // Remove Task Manager's cached decision for our own entry. Windows rebuilds it
        // from the Run value, so enabling also recovers from a previously disabled item.
        using var approvalKey = Registry.CurrentUser.OpenSubKey(
            _startupApprovedKeyPath,
            writable: true);
        approvalKey?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    internal static string BuildCommand(string executablePath) =>
        $"\"{Path.GetFullPath(executablePath)}\"";

    internal static bool CommandTargetsExecutable(string? command, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var value = command.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(value),
                Path.GetFullPath(executablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool IsExplicitlyDisabled(byte[]? startupApprovedValue) =>
        startupApprovedValue is { Length: > 0 } && startupApprovedValue[0] == 3;
}
