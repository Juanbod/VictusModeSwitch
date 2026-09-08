using System.Management;

namespace VictusModeSwitch;

internal static class HardwareInfo
{
    public const string SupportedBoard = "8A4F";

    public static string GetBoardProduct()
    {
        using var searcher = new ManagementObjectSearcher(
            "root\\cimv2",
            "SELECT Product FROM Win32_BaseBoard");

        foreach (ManagementObject board in searcher.Get())
        {
            using (board)
            {
                return Convert.ToString(board["Product"])?.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    public static string GetComputerModel()
    {
        using var searcher = new ManagementObjectSearcher(
            "root\\cimv2",
            "SELECT Model FROM Win32_ComputerSystem");

        foreach (ManagementObject computer in searcher.Get())
        {
            using (computer)
            {
                return Convert.ToString(computer["Model"])?.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    public static void EnsureSupportedBoard()
    {
        var board = GetBoardProduct();
        if (!string.Equals(board, SupportedBoard, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Запись остановлена: ожидалась системная плата {SupportedBoard}, обнаружена '{board}'.");
        }
    }
}
