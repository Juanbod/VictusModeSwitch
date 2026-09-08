using System.Text;

namespace VictusModeSwitch;

internal static class Log
{
    private static readonly object Sync = new();
    private const long MaxLogSize = 1024 * 1024;

    public static void Info(string message) => Write("INFO", message);
    public static void Warning(string message) => Write("WARN", message);
    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception}");

    private static void Write(string level, string message)
    {
        lock (Sync)
        {
            try
            {
                AppPaths.EnsureCreated();
                RotateIfNeeded();
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}";
                using var stream = new FileStream(
                    AppPaths.LogFile,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(line);
            }
            catch
            {
                // Logging must never prevent a mode switch.
            }
        }
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(AppPaths.LogFile);
        if (!file.Exists || file.Length < MaxLogSize)
        {
            return;
        }

        var previous = AppPaths.LogFile + ".old";
        File.Move(AppPaths.LogFile, previous, true);
    }
}
