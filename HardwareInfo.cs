using System.Management;

namespace VictusModeSwitch;

internal static class HardwareInfo
{
    public const string SupportedBoard = "8A4F";
    private static string? _cachedBoardProduct;

    public static string GetBoardProduct()
    {
        var cached = Volatile.Read(ref _cachedBoardProduct);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        using var searcher = new ManagementObjectSearcher(
            "root\\cimv2",
            "SELECT Product FROM Win32_BaseBoard");

        foreach (ManagementObject board in searcher.Get())
        {
            using (board)
            {
                var product = Convert.ToString(board["Product"])?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(product))
                {
                    Interlocked.CompareExchange(ref _cachedBoardProduct, product, null);
                }

                return product;
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
