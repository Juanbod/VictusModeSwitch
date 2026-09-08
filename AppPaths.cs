namespace VictusModeSwitch;

internal static class AppPaths
{
    public static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VictusModeSwitch");

    public static readonly string SettingsFile = Path.Combine(DataDirectory, "settings.json");
    public static readonly string LogFile = Path.Combine(DataDirectory, "VictusModeSwitch.log");
    public static readonly string OmenTaskBackupFile = Path.Combine(DataDirectory, "omen-task-backup.json");
    public static readonly string UpdateDirectory = Path.Combine(DataDirectory, "Updates");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(UpdateDirectory);
    }
}
