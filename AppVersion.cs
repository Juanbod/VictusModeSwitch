using System.Reflection;

namespace VictusModeSwitch;

internal static class AppVersion
{
    public static Version Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static string Display => $"{Current.Major}.{Current.Minor}.{Current.Build}";
}
