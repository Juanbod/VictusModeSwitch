using System.Runtime.InteropServices;

namespace VictusModeSwitch;

internal static class DisplayRefreshRateController
{
    private const uint QueryOnlyActivePaths = 0x00000002;
    private const int ErrorInsufficientBuffer = 122;
    private const uint DisplayConfigDeviceInfoGetSourceName = 1;
    private const int DisplayPortEmbedded = 11;
    private const int UdiEmbedded = 13;
    private const int Internal = unchecked((int)0x80000000);
    private const int EnumCurrentSettings = -1;
    private const uint DmDisplayFrequency = 0x00400000;
    private const uint CdsUpdateRegistry = 0x00000001;
    private const uint CdsTest = 0x00000002;
    private const int DispChangeSuccessful = 0;

    public static DisplayRefreshRateBackup Capture()
    {
        var deviceName = GetInternalDisplayDeviceName();
        var mode = GetCurrentMode(deviceName);
        if (mode.DisplayFrequency <= 1)
        {
            throw new InvalidOperationException("Windows не вернула частоту встроенного дисплея.");
        }

        return new DisplayRefreshRateBackup
        {
            Valid = true,
            DeviceName = deviceName,
            Frequency = mode.DisplayFrequency
        };
    }

    public static void ApplyLimit(DisplayRefreshRateBackup backup, uint maximumFrequency = 60)
    {
        EnsureValid(backup);
        var current = GetCurrentMode(backup.DeviceName).DisplayFrequency;
        var target = GetLimitedFrequency(current, maximumFrequency);
        if (target != current)
        {
            SetFrequency(backup.DeviceName, target);
        }

        Log.Info($"Eco display limit: {backup.DeviceName}, {current} -> {target} Hz");
    }

    public static void Restore(DisplayRefreshRateBackup backup)
    {
        EnsureValid(backup);
        var current = GetCurrentMode(backup.DeviceName).DisplayFrequency;
        if (current != backup.Frequency)
        {
            SetFrequency(backup.DeviceName, backup.Frequency);
        }

        Log.Info($"Eco display limit: restored {backup.DeviceName} to {backup.Frequency} Hz");
    }

    internal static uint GetLimitedFrequency(uint current, uint maximum) =>
        current > maximum ? maximum : current;

    private static string GetInternalDisplayDeviceName()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = GetDisplayConfigBufferSizes(
                QueryOnlyActivePaths,
                out var pathCount,
                out var modeCount);
            CheckWin32(result, "получение конфигурации дисплеев");

            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            result = QueryDisplayConfig(
                QueryOnlyActivePaths,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                IntPtr.Zero);
            if (result == ErrorInsufficientBuffer)
            {
                continue;
            }

            CheckWin32(result, "чтение конфигурации дисплеев");
            foreach (var path in paths.Take((int)pathCount))
            {
                if (!IsInternal(path.TargetInfo.OutputTechnology))
                {
                    continue;
                }

                var sourceName = new DisplayConfigSourceDeviceName
                {
                    Header = new DisplayConfigDeviceInfoHeader
                    {
                        Type = DisplayConfigDeviceInfoGetSourceName,
                        Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                        AdapterId = path.SourceInfo.AdapterId,
                        Id = path.SourceInfo.Id
                    },
                    ViewGdiDeviceName = string.Empty
                };
                CheckWin32(DisplayConfigGetDeviceInfo(ref sourceName), "определение встроенного дисплея");
                if (!string.IsNullOrWhiteSpace(sourceName.ViewGdiDeviceName))
                {
                    return sourceName.ViewGdiDeviceName;
                }
            }

            throw new InvalidOperationException("Активный встроенный дисплей не найден.");
        }

        throw new InvalidOperationException("Конфигурация дисплеев изменилась во время чтения.");
    }

    private static bool IsInternal(int technology) =>
        technology is DisplayPortEmbedded or UdiEmbedded or Internal;

    private static DevMode GetCurrentMode(string deviceName)
    {
        var mode = NewDevMode();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode))
        {
            throw new InvalidOperationException(
                $"Windows не смогла прочитать режим дисплея '{deviceName}', код {Marshal.GetLastWin32Error()}.");
        }

        return mode;
    }

    private static void SetFrequency(string deviceName, uint frequency)
    {
        var mode = GetCurrentMode(deviceName);
        mode.Fields = DmDisplayFrequency;
        mode.DisplayFrequency = frequency;

        CheckDisplayChange(
            ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsTest, IntPtr.Zero),
            $"проверка частоты {frequency} Гц");
        CheckDisplayChange(
            ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero),
            $"установка частоты {frequency} Гц");
    }

    private static DevMode NewDevMode() => new()
    {
        DeviceName = string.Empty,
        FormName = string.Empty,
        Size = (ushort)Marshal.SizeOf<DevMode>()
    };

    private static void EnsureValid(DisplayRefreshRateBackup backup)
    {
        if (!backup.Valid || string.IsNullOrWhiteSpace(backup.DeviceName) || backup.Frequency <= 1)
        {
            throw new InvalidOperationException("Нет резервной копии частоты встроенного дисплея.");
        }
    }

    private static void CheckWin32(int result, string operation)
    {
        if (result != 0)
        {
            throw new InvalidOperationException($"Ошибка Windows при операции '{operation}', код {result}.");
        }
    }

    private static void CheckDisplayChange(int result, string operation)
    {
        if (result != DispChangeSuccessful)
        {
            throw new InvalidOperationException($"Ошибка Windows при операции '{operation}', код {result}.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public int OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct DisplayConfigModeInfoUnion
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public DisplayConfigModeInfoUnion ModeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TtOption;
        public short Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;

        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(
        string deviceName,
        int modeNumber,
        ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string deviceName,
        ref DevMode devMode,
        IntPtr window,
        uint flags,
        IntPtr parameters);
}
