using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace VictusModeSwitch;

internal sealed class OmenKeyListener : IDisposable
{
    public const uint OmenEventId = 29;
    public const uint OmenEventData = 8613;

    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfExtended = 0x01;

    private const uint VkF12 = 0x7B;
    private const uint VkF24 = 0x87;
    private const uint VkOmen157 = 0x9D;
    private const uint VkOemOmen = 0xFF;

    private readonly object _watcherSync = new();
    private ManagementEventWatcher? _watcher;
    private KeyboardHookProc? _hookProc;
    private IntPtr _hookHandle;
    private long _lastPressTicks;
    private int _keyboardPressed;
    private volatile bool _keyboardHookActive;
    private volatile bool _disposed;
    private readonly bool _diagnosticLogging;

    public OmenKeyListener(bool diagnosticLogging = false)
    {
        _diagnosticLogging = diagnosticLogging;
    }

    public event Action<uint, uint>? EventObserved;
    public event Action? OmenKeyPressed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var keyboardStarted = StartKeyboardHook();
        if (keyboardStarted)
        {
            Log.Info("OMEN key: основной keyboard hook активен, HP WMI запускается в фоне как резерв");
        }
        else
        {
            Log.Warning("OMEN key: keyboard hook недоступен, запускается резервный HP WMI");
        }

        _ = Task.Run(() =>
        {
            var wmiStarted = StartWmiListener();
            if (!keyboardStarted && wmiStarted)
            {
                Log.Warning("OMEN key: используется резервный канал HP WMI");
            }
            else if (!keyboardStarted && !wmiStarted && !_disposed)
            {
                Log.Warning("OMEN key: ни keyboard hook, ни HP WMI не удалось запустить");
            }
        });
    }

    private bool StartWmiListener()
    {
        ManagementEventWatcher? watcher = null;
        try
        {
            var scope = new ManagementScope(
                "\\\\.\\root\\wmi",
                new ConnectionOptions { EnablePrivileges = true });
            scope.Connect();
            watcher = new ManagementEventWatcher(scope, new WqlEventQuery("SELECT * FROM hpqBEvnt"));
            watcher.EventArrived += OnEventArrived;
            watcher.Start();

            lock (_watcherSync)
            {
                if (!_disposed)
                {
                    _watcher = watcher;
                    watcher = null;
                }
            }

            if (watcher is not null)
            {
                return false;
            }

            Log.Info("Слушатель HP WMI запущен: EventID=29, EventData=8613, EnablePrivileges=true");
            return true;
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                Log.Error("HP WMI-события недоступны", exception);
            }

            return false;
        }
        finally
        {
            if (watcher is not null)
            {
                watcher.EventArrived -= OnEventArrived;
                try
                {
                    watcher.Stop();
                }
                catch
                {
                }

                watcher.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        ManagementEventWatcher? watcher;
        lock (_watcherSync)
        {
            watcher = _watcher;
            _watcher = null;
        }

        if (watcher is not null)
        {
            try
            {
                watcher.Stop();
            }
            catch (Exception exception)
            {
                Log.Warning($"Не удалось штатно остановить HP WMI listener: {exception.Message}");
            }

            watcher.EventArrived -= OnEventArrived;
            watcher.Dispose();
        }

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _keyboardHookActive = false;
        _hookProc = null;
    }

    private bool StartKeyboardHook()
    {
        _hookProc = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = GetModuleHandle(module?.ModuleName);
        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, moduleHandle, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            Log.Warning($"Клавиатурный hook не запущен, код Windows {Marshal.GetLastWin32Error()}");
            _hookProc = null;
            return false;
        }

        _keyboardHookActive = true;
        Log.Info("Low-level keyboard hook запущен");
        return true;
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var eventId = Convert.ToUInt32(eventArgs.NewEvent["EventID"]);
            var eventData = Convert.ToUInt32(eventArgs.NewEvent["EventData"]);
            EventObserved?.Invoke(eventId, eventData);
            if (eventId == OmenEventId && eventData == OmenEventData)
            {
                var active = eventArgs.NewEvent.Properties["Active"]?.Value;
                if (!_keyboardHookActive)
                {
                    SignalOmenKey($"WMI Active={active ?? "n/a"}");
                }
            }
        }
        catch (Exception exception)
        {
            Log.Error("Ошибка обработки HP WMI-события", exception);
        }
    }

    private IntPtr HookCallback(int code, IntPtr messagePointer, IntPtr dataPointer)
    {
        if (code < 0)
        {
            return CallNextHookEx(_hookHandle, code, messagePointer, dataPointer);
        }

        var message = messagePointer.ToInt32();
        var data = Marshal.PtrToStructure<KeyboardHookData>(dataPointer);
        var normalizedScan = (data.Flags & LlkhfExtended) != 0
            ? 0xE000u | data.ScanCode
            : data.ScanCode;
        var isOmen = IsHighConfidenceOmenKey(data.VirtualKey, normalizedScan);

        if (_diagnosticLogging && (message == WmKeyDown || message == WmSysKeyDown) &&
            (data.VirtualKey >= 0x70 || (data.Flags & LlkhfExtended) != 0))
        {
            Log.Info($"KEY DIAG: VK=0x{data.VirtualKey:X2}, Scan=0x{normalizedScan:X4}, Flags=0x{data.Flags:X}");
        }

        if (!isOmen)
        {
            return CallNextHookEx(_hookHandle, code, messagePointer, dataPointer);
        }

        if (message == WmKeyDown || message == WmSysKeyDown)
        {
            if (Interlocked.Exchange(ref _keyboardPressed, 1) == 0)
            {
                SignalOmenKey($"keyboard VK=0x{data.VirtualKey:X2} Scan=0x{normalizedScan:X4}");
            }

            return new IntPtr(1);
        }

        if (message == WmKeyUp || message == WmSysKeyUp)
        {
            Interlocked.Exchange(ref _keyboardPressed, 0);
            return new IntPtr(1);
        }

        return CallNextHookEx(_hookHandle, code, messagePointer, dataPointer);
    }

    private static bool IsHighConfidenceOmenKey(uint virtualKey, uint scanCode)
    {
        var dedicatedScan = scanCode is 0xE045 or 0xE046 or 0x0046 or 0x009D;
        if (virtualKey == VkF12 && scanCode == 0xE045)
        {
            return true;
        }

        return dedicatedScan && virtualKey is VkF24 or VkOmen157 or VkOemOmen;
    }

    private void SignalOmenKey(string source)
    {
        var now = Environment.TickCount64;
        var previous = Interlocked.Exchange(ref _lastPressTicks, now);
        if (previous != 0 && now - previous < 80)
        {
            return;
        }

        Log.Info($"Получено нажатие OMEN key через {source}");
        OmenKeyPressed?.Invoke();
    }

    private delegate IntPtr KeyboardHookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        KeyboardHookProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
