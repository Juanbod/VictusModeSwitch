using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VictusModeSwitch;

internal sealed record ThemePalette(
    bool Dark,
    Color Background,
    Color Surface,
    Color Border,
    Color Text,
    Color SecondaryText,
    Color Accent,
    Color AccentText);

internal static class WindowsTheme
{
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    public static ThemePalette Current
    {
        get
        {
            var dark = IsDarkMode();
            var accent = GetAccentColor();
            return dark
                ? new ThemePalette(
                    true,
                    Color.FromArgb(32, 32, 32),
                    Color.FromArgb(43, 43, 43),
                    Color.FromArgb(61, 61, 61),
                    Color.FromArgb(255, 255, 255),
                    Color.FromArgb(200, 200, 200),
                    accent,
                    Color.White)
                : new ThemePalette(
                    false,
                    Color.FromArgb(243, 243, 243),
                    Color.FromArgb(251, 251, 251),
                    Color.FromArgb(224, 224, 224),
                    Color.FromArgb(26, 26, 26),
                    Color.FromArgb(92, 92, 92),
                    accent,
                    Color.White);
        }
    }

    public static Font Font(float size, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI Variable Text", size, style, GraphicsUnit.Point);

    public static bool AnimationsEnabled
    {
        get
        {
            try
            {
                return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var enabled, 0) || enabled;
            }
            catch
            {
                return true;
            }
        }
    }

    public static void ApplyWindow(Form form, bool mica = true)
    {
        ApplyNativeWindow(form.Handle, mica);
    }

    public static void ApplyNativeWindow(IntPtr window, bool mica = false)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return;
        }

        var dark = Current.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(window, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var rounded = 2;
            _ = DwmSetWindowAttribute(window, DwmwaWindowCornerPreference, ref rounded, sizeof(int));
            if (mica)
            {
                var mainWindowBackdrop = 2;
                _ = DwmSetWindowAttribute(window, DwmwaSystemBackdropType, ref mainWindowBackdrop, sizeof(int));
            }
        }
    }

    public static void RoundControl(Control control, int radius)
    {
        if (control.Width <= 0 || control.Height <= 0)
        {
            return;
        }

        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(control.Width - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(control.Width - diameter, control.Height - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, control.Height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        control.Region?.Dispose();
        control.Region = new Region(path);
    }

    private static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Color GetAccentColor()
    {
        try
        {
            if (DwmGetColorizationColor(out var rawColor, out _) == 0)
            {
                var red = (byte)(rawColor >> 16);
                var green = (byte)(rawColor >> 8);
                var blue = (byte)rawColor;
                var color = Color.FromArgb(red, green, blue);
                if (color.GetBrightness() is > 0.22f and < 0.78f)
                {
                    return color;
                }
            }
        }
        catch
        {
            // Fall through to the standard Windows 11 blue.
        }

        return Color.FromArgb(0, 103, 192);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint colorization, out bool opaqueBlend);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out bool value, uint flags);
}
