using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neurocorp.Api.Web.Middleware;

public class TimeOnlyJsonConverterFactory : JsonConverterFactory
{
    private static readonly string[] Formats = ["HH:mm", "HH:mm:ss"];

    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(TimeOnly) || typeToConvert == typeof(TimeOnly?);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(TimeOnly?))
            return new NullableTimeOnlyConverter();
        return new TimeOnlyConverter();
    }

    private class TimeOnlyConverter : JsonConverter<TimeOnly>
    {
        public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString()
                ?? throw new JsonException("Expected a string value for TimeOnly.");
            return Parse(value);
        }

        public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString("HH:mm:ss"));
        }
    }

    private class NullableTimeOnlyConverter : JsonConverter<TimeOnly?>
    {
        public override TimeOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;
            var value = reader.GetString()
                ?? throw new JsonException("Expected a string value for TimeOnly.");
            return Parse(value);
        }

        public override void Write(Utf8JsonWriter writer, TimeOnly? value, JsonSerializerOptions options)
        {
            if (value is null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value.Value.ToString("HH:mm:ss"));
        }
    }

    private static TimeOnly Parse(string value)
    {
        if (TimeOnly.TryParseExact(value, Formats, null, System.Globalization.DateTimeStyles.None, out var result))
            return result;
        throw new JsonException($"Unable to parse '{value}' as TimeOnly. Expected format: HH:mm or HH:mm:ss.");
    }
}
