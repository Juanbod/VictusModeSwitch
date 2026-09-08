using System.Runtime.InteropServices;

namespace VictusModeSwitch;

internal static class NvidiaFrameRateController
{
    private const uint FrameRateLimiterSettingId = 0x10835002;
    private const uint MaximumSupportedLimit = 0x000003ff;
    private const int NvApiOk = 0;
    private const int NvApiSettingNotFound = -160;

    // NVDRS_SETTING_V1 from NVIDIA's MIT-licensed NVAPI headers, packed on 4-byte boundaries.
    private const int SettingSize = 12320;
    private const int SettingVersion = (1 << 16) | SettingSize;
    private const int SettingIdOffset = 4100;
    private const int SettingTypeOffset = 4104;
    private const int CurrentDwordValueOffset = 8220;

    public static NvidiaFrameRateBackup Capture()
    {
        using var api = new NvApiSession();
        var limit = api.GetDwordSetting(FrameRateLimiterSettingId);
        return new NvidiaFrameRateBackup { Valid = true, Limit = limit };
    }

    public static void ApplyLimit(NvidiaFrameRateBackup backup, uint maximumFrameRate = 60)
    {
        EnsureValid(backup);
        var target = GetEcoLimit(backup.Limit, maximumFrameRate);
        using var api = new NvApiSession();
        api.SetDwordSetting(FrameRateLimiterSettingId, target);
        Log.Info($"Eco NVIDIA limit: {backup.Limit} -> {target} FPS");
    }

    public static void Restore(NvidiaFrameRateBackup backup)
    {
        EnsureValid(backup);
        using var api = new NvApiSession();
        api.SetDwordSetting(FrameRateLimiterSettingId, backup.Limit);
        Log.Info($"Eco NVIDIA limit: restored {backup.Limit} FPS");
    }

    internal static uint GetEcoLimit(uint currentLimit, uint maximumFrameRate) =>
        currentLimit == 0 ? maximumFrameRate : Math.Min(currentLimit, maximumFrameRate);

    private static void EnsureValid(NvidiaFrameRateBackup backup)
    {
        if (!backup.Valid || backup.Limit > MaximumSupportedLimit)
        {
            throw new InvalidOperationException("Нет корректной резервной копии лимита NVIDIA.");
        }
    }

    private sealed class NvApiSession : IDisposable
    {
        private readonly StatusFunction _unload;
        private readonly SessionFunction _destroySession;
        private readonly GetSettingFunction _getSetting;
        private readonly SetSettingFunction _setSetting;
        private readonly SessionFunction _saveSettings;
        private IntPtr _session;
        private IntPtr _baseProfile;
        private bool _initialized;

        public NvApiSession()
        {
            var initialize = Resolve<StatusFunction>(0x0150e828);
            _unload = Resolve<StatusFunction>(0xd22bdd7e);
            var createSession = Resolve<CreateSessionFunction>(0x0694d52e);
            _destroySession = Resolve<SessionFunction>(0xdad9cff8);
            var loadSettings = Resolve<SessionFunction>(0x375dbd6b);
            _saveSettings = Resolve<SessionFunction>(0xfcbc7e14);
            _getSetting = Resolve<GetSettingFunction>(0x73bf8338);
            _setSetting = Resolve<SetSettingFunction>(0x577dd202);
            var getBaseProfile = Resolve<GetBaseProfileFunction>(0xda8466a0);

            Check(initialize(), "инициализация NVIDIA API");
            _initialized = true;
            try
            {
                Check(createSession(out _session), "создание сессии NVIDIA");
                Check(loadSettings(_session), "чтение профилей NVIDIA");
                Check(getBaseProfile(_session, out _baseProfile), "получение глобального профиля NVIDIA");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public uint GetDwordSetting(uint settingId)
        {
            var setting = AllocateSetting();
            try
            {
                var result = _getSetting(_session, _baseProfile, settingId, setting);
                if (result == NvApiSettingNotFound)
                {
                    return 0;
                }

                Check(result, "чтение ограничения частоты кадров NVIDIA");
                var settingType = Marshal.ReadInt32(setting, SettingTypeOffset);
                if (settingType != 0)
                {
                    throw new InvalidOperationException(
                        $"NVIDIA вернула неожиданный тип настройки FPS: {settingType}.");
                }

                var value = unchecked((uint)Marshal.ReadInt32(setting, CurrentDwordValueOffset));
                if (value > MaximumSupportedLimit)
                {
                    throw new InvalidOperationException($"NVIDIA вернула некорректный лимит FPS: {value}.");
                }

                return value;
            }
            finally
            {
                Marshal.FreeHGlobal(setting);
            }
        }

        public void SetDwordSetting(uint settingId, uint value)
        {
            if (value > MaximumSupportedLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            var setting = AllocateSetting();
            try
            {
                Marshal.WriteInt32(setting, SettingIdOffset, unchecked((int)settingId));
                Marshal.WriteInt32(setting, SettingTypeOffset, 0);
                Marshal.WriteInt32(setting, CurrentDwordValueOffset, unchecked((int)value));
                Check(_setSetting(_session, _baseProfile, setting), "запись ограничения FPS NVIDIA");
                Check(_saveSettings(_session), "сохранение профилей NVIDIA");
            }
            finally
            {
                Marshal.FreeHGlobal(setting);
            }
        }

        public void Dispose()
        {
            if (_session != IntPtr.Zero)
            {
                _destroySession(_session);
                _session = IntPtr.Zero;
            }

            if (_initialized)
            {
                _unload();
                _initialized = false;
            }
        }

        private static IntPtr AllocateSetting()
        {
            var pointer = Marshal.AllocHGlobal(SettingSize);
            Marshal.Copy(new byte[SettingSize], 0, pointer, SettingSize);
            Marshal.WriteInt32(pointer, SettingVersion);
            return pointer;
        }

        private static T Resolve<T>(uint functionId) where T : Delegate
        {
            var pointer = NvApiQueryInterface(functionId);
            if (pointer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"Драйвер NVIDIA не предоставляет функцию NVAPI 0x{functionId:X8}.");
            }

            return Marshal.GetDelegateForFunctionPointer<T>(pointer);
        }

        private static void Check(int status, string operation)
        {
            if (status != NvApiOk)
            {
                throw new InvalidOperationException($"Ошибка NVIDIA API при операции '{operation}', код {status}.");
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StatusFunction();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CreateSessionFunction(out IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SessionFunction(IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetBaseProfileFunction(IntPtr session, out IntPtr profile);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetSettingFunction(
        IntPtr session,
        IntPtr profile,
        uint settingId,
        IntPtr setting);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetSettingFunction(IntPtr session, IntPtr profile, IntPtr setting);

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvApiQueryInterface(uint functionId);
}
