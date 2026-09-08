using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace VictusModeSwitch;

[Flags]
internal enum GlobalHotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

internal sealed record HotkeyBinding
{
    private const GlobalHotkeyModifiers AllowedModifiers =
        GlobalHotkeyModifiers.Alt |
        GlobalHotkeyModifiers.Control |
        GlobalHotkeyModifiers.Shift |
        GlobalHotkeyModifiers.Windows;

    public int VirtualKey { get; init; }
    public GlobalHotkeyModifiers Modifiers { get; init; }

    [JsonIgnore]
    public bool IsConfigured => VirtualKey != 0 && Modifiers != GlobalHotkeyModifiers.None;

    public HotkeyBinding Normalize()
    {
        var modifiers = Modifiers & AllowedModifiers;
        if (VirtualKey is < 0x08 or > 0xFE ||
            modifiers == GlobalHotkeyModifiers.None ||
            IsModifierKey(VirtualKey))
        {
            return new HotkeyBinding();
        }

        return new HotkeyBinding { VirtualKey = VirtualKey, Modifiers = modifiers };
    }

    public string ToDisplayString()
    {
        var binding = Normalize();
        if (!binding.IsConfigured)
        {
            return string.Empty;
        }

        var parts = new List<string>(5);
        if (binding.Modifiers.HasFlag(GlobalHotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (binding.Modifiers.HasFlag(GlobalHotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (binding.Modifiers.HasFlag(GlobalHotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (binding.Modifiers.HasFlag(GlobalHotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(GetKeyName(binding.VirtualKey));
        return string.Join(" + ", parts);
    }

    public static HotkeyBinding FromKeyData(Keys keyData, bool windowsKeyDown)
    {
        var modifiers = GlobalHotkeyModifiers.None;
        if (keyData.HasFlag(Keys.Control))
        {
            modifiers |= GlobalHotkeyModifiers.Control;
        }

        if (keyData.HasFlag(Keys.Alt))
        {
            modifiers |= GlobalHotkeyModifiers.Alt;
        }

        if (keyData.HasFlag(Keys.Shift))
        {
            modifiers |= GlobalHotkeyModifiers.Shift;
        }

        if (windowsKeyDown)
        {
            modifiers |= GlobalHotkeyModifiers.Windows;
        }

        return new HotkeyBinding
        {
            VirtualKey = (int)(keyData & Keys.KeyCode),
            Modifiers = modifiers
        }.Normalize();
    }

    public static bool IsModifierKey(int virtualKey) => virtualKey is
        (int)Keys.ShiftKey or
        (int)Keys.ControlKey or
        (int)Keys.Menu or
        (int)Keys.LShiftKey or
        (int)Keys.RShiftKey or
        (int)Keys.LControlKey or
        (int)Keys.RControlKey or
        (int)Keys.LMenu or
        (int)Keys.RMenu or
        (int)Keys.LWin or
        (int)Keys.RWin;

    private static string GetKeyName(int virtualKey)
    {
        if (virtualKey is >= (int)Keys.D0 and <= (int)Keys.D9)
        {
            return ((char)('0' + virtualKey - (int)Keys.D0)).ToString();
        }

        if (virtualKey is >= (int)Keys.NumPad0 and <= (int)Keys.NumPad9)
        {
            return $"Num {virtualKey - (int)Keys.NumPad0}";
        }

        return (Keys)virtualKey switch
        {
            Keys.Return => "Enter",
            Keys.Escape => "Esc",
            Keys.Prior => "Page Up",
            Keys.Next => "Page Down",
            Keys.Snapshot => "Print Screen",
            Keys.Space => "Space",
            _ => new KeysConverter().ConvertToInvariantString((Keys)virtualKey) ?? $"VK {virtualKey:X2}"
        };
    }
}

internal readonly record struct HotkeyRegistrationResult(bool Success, int ErrorCode)
{
    public const int AlreadyRegisteredError = 1409;

    public static HotkeyRegistrationResult Registered() => new(true, 0);
    public static HotkeyRegistrationResult Failed(int errorCode) => new(false, errorCode);
}

internal sealed class GlobalHotkeyController : NativeWindow, IDisposable
{
    private const int HotkeyId = 0x564D;
    private const int WmHotkey = 0x0312;
    private const uint NoRepeat = 0x4000;
    private static readonly IntPtr MessageOnlyWindow = new(-3);
    private bool _registered;
    private bool _disposed;
    private long _suppressUntil;
    private readonly bool _writeLog;

    public GlobalHotkeyController(bool writeLog = true)
    {
        _writeLog = writeLog;
        // RegisterHotKey delivers WM_HOTKEY here without requiring a visible form.
        CreateHandle(new CreateParams
        {
            Caption = "VictusModeSwitch.GlobalHotkey",
            Parent = MessageOnlyWindow
        });
    }

    public event Action? Pressed;

    public HotkeyBinding Binding { get; private set; } = new();
    public bool SuppressInvocations { get; set; }

    public HotkeyRegistrationResult Apply(HotkeyBinding requestedBinding)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var binding = requestedBinding.Normalize();
        _suppressUntil = Environment.TickCount64 + 350;

        if (binding == Binding && (!binding.IsConfigured || _registered))
        {
            return HotkeyRegistrationResult.Registered();
        }

        var previousBinding = Binding;
        var previousWasRegistered = _registered;
        Unregister();

        if (!binding.IsConfigured)
        {
            Binding = binding;
            return HotkeyRegistrationResult.Registered();
        }

        if (Register(binding))
        {
            Binding = binding;
            _registered = true;
            if (_writeLog)
            {
                Log.Info($"Глобальный хоткей зарегистрирован: {binding.ToDisplayString()}");
            }

            return HotkeyRegistrationResult.Registered();
        }

        var error = Marshal.GetLastWin32Error();
        Binding = previousBinding;
        if (previousWasRegistered && previousBinding.IsConfigured)
        {
            _registered = Register(previousBinding);
            if (!_registered && _writeLog)
            {
                Log.Warning($"Не удалось восстановить прежний глобальный хоткей, Win32={Marshal.GetLastWin32Error()}");
            }
        }

        if (_writeLog)
        {
            Log.Warning($"Не удалось зарегистрировать глобальный хоткей {binding.ToDisplayString()}, Win32={error}");
        }

        return HotkeyRegistrationResult.Failed(error);
    }

    public static bool IsWindowsKeyDown() =>
        (GetAsyncKeyState((int)Keys.LWin) & 0x8000) != 0 ||
        (GetAsyncKeyState((int)Keys.RWin) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        DestroyHandle();
        GC.SuppressFinalize(this);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey &&
            message.WParam.ToInt32() == HotkeyId &&
            !SuppressInvocations &&
            Environment.TickCount64 >= _suppressUntil)
        {
            Pressed?.Invoke();
        }

        base.WndProc(ref message);
    }

    private bool Register(HotkeyBinding binding) => RegisterHotKey(
        Handle,
        HotkeyId,
        (uint)binding.Modifiers | NoRepeat,
        (uint)binding.VirtualKey);

    private void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
