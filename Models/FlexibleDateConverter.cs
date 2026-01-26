using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CsvIntegratorApp.Models
{
    internal sealed class FlexibleDateConverter : JsonConverter<DateTime?>
    {
        private static readonly string[] Formats =
        {
            "dd/MM/yyyy",
            "yyyy-MM-dd",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssK",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss.fffK"
        };

        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Invalid date value.");
            }

            var value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTime.TryParseExact(value, Formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            {
                return parsed.Date;
            }

            if (DateTime.TryParse(value, PtBr, DateTimeStyles.AssumeLocal, out parsed))
            {
                return parsed.Date;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
            {
                return parsed.Date;
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (!value.HasValue)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        }
    }
}
