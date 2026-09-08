using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VictusModeSwitch;

[JsonConverter(typeof(AppModeJsonConverter))]
internal enum AppMode
{
    Eco,
    Standard,
    Performance
}

internal static class AppModeExtensions
{
    public static AppMode TogglePerformance(this AppMode mode) => mode switch
    {
        AppMode.Performance => AppMode.Standard,
        AppMode.Standard => AppMode.Performance,
        _ => AppMode.Standard
    };

    public static Color AccentColor(this AppMode mode) => mode switch
    {
        AppMode.Eco => Color.FromArgb(55, 166, 105),
        AppMode.Standard => Color.FromArgb(52, 119, 235),
        AppMode.Performance => Color.FromArgb(225, 76, 65),
        _ => Color.Gray
    };
}

internal sealed class AppModeJsonConverter : JsonConverter<AppMode>
{
    public override AppMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (string.Equals(value, "Gaming", StringComparison.OrdinalIgnoreCase))
            {
                return AppMode.Performance;
            }

            if (Enum.TryParse<AppMode>(value, true, out var mode))
            {
                return mode;
            }
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numeric))
        {
            return numeric switch
            {
                0 => AppMode.Eco,
                1 => AppMode.Standard,
                2 or 3 => AppMode.Performance,
                _ => AppMode.Standard
            };
        }

        throw new JsonException("Unknown performance mode.");
    }

    public override void Write(Utf8JsonWriter writer, AppMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
