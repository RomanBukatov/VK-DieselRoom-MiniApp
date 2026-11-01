using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DieselRoom.Api.Json;

/// <summary>
/// Общие правила для дат в API: на выходе ВСЕГДА строгий ISO 8601 в UTC с миллисекундами
/// (<c>yyyy-MM-ddTHH:mm:ss.fffZ</c>). Такой формат одинаково разбирается всеми движками, включая
/// старый iOS WebKit (который падает на строках без 'T' / без часового пояса / с 7 знаками дробной части).
/// </summary>
public static class UtcDateTimeFormat
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>
    /// Unspecified трактуем как UTC (так хранятся значения timestamptz в Npgsql и так их пишут сервисы),
    /// Local переводим в UTC.
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    public static string ToString(DateTime value) => ToUtc(value).ToString(Format, CultureInfo.InvariantCulture);

    public static bool TryParse(string? text, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Строки без часового пояса считаем UTC; со смещением — переводим в UTC
        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out value);
    }
}

public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (UtcDateTimeFormat.TryParse(text, out var value))
        {
            return value;
        }

        throw new JsonException($"Некорректный формат даты: '{text}'.");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(UtcDateTimeFormat.ToString(value));
}

public sealed class NullableUtcDateTimeConverter : JsonConverter<DateTime?>
{
    public override bool HandleNull => true;

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (UtcDateTimeFormat.TryParse(text, out var value))
        {
            return value;
        }

        throw new JsonException($"Некорректный формат даты: '{text}'.");
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(UtcDateTimeFormat.ToString(value.Value));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
