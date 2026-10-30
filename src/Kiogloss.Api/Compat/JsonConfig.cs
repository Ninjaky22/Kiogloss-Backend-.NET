using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kiogloss.Api.Compat;

/// <summary>
/// System.Text.Json configured to read and write JSON like Spring Boot's Jackson ObjectMapper:
/// property names exactly as declared, nulls included, raw UTF-8, doubles as Double#toString,
/// LocalDateTime as ISO_LOCAL_DATE_TIME and Jackson's lenient scalar coercions on input.
/// </summary>
public static class JsonConfig
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions());

    public static JsonSerializerOptions Configure(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = null;
        o.DictionaryKeyPolicy = null;
        o.PropertyNameCaseInsensitive = false;
        o.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        o.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        o.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
        o.NumberHandling = JsonNumberHandling.Strict;
        o.Converters.Add(new LenientInt32Converter());
        o.Converters.Add(new LenientNullableInt32Converter());
        o.Converters.Add(new LenientInt64Converter());
        o.Converters.Add(new LenientNullableInt64Converter());
        o.Converters.Add(new JavaDoubleConverter());
        o.Converters.Add(new JavaNullableDoubleConverter());
        o.Converters.Add(new LenientDecimalConverter());
        o.Converters.Add(new LenientNullableDecimalConverter());
        o.Converters.Add(new LenientBooleanConverter());
        o.Converters.Add(new LenientNullableBooleanConverter());
        o.Converters.Add(new LenientStringConverter());
        o.Converters.Add(new JacksonLocalDateTimeConverter());
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    internal static string TokenText(ref Utf8JsonReader reader) =>
        reader.HasValueSequence
            ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
            : Encoding.UTF8.GetString(reader.ValueSpan);

    internal static JsonException CannotDeserialize(string javaType, ref Utf8JsonReader reader) =>
        new($"Cannot deserialize value of type `{javaType}` from {Describe(ref reader)}");

    private static string Describe(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => $"String \"{reader.GetString()}\": not a valid representation",
        JsonTokenType.StartObject => "Object value (token `JsonToken.START_OBJECT`)",
        JsonTokenType.StartArray => "Array value (token `JsonToken.START_ARRAY`)",
        JsonTokenType.True or JsonTokenType.False => "Boolean value",
        _ => reader.TokenType.ToString(),
    };
}

// ---------------------------------------------------------------- numbers (Jackson coercions)

internal static class Lenient
{
    /// <summary>Returns null for JSON null and for "" (Jackson coerces empty String to null for wrappers).</summary>
    public static string? ScalarText(ref Utf8JsonReader reader, string javaType)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return JsonConfig.TokenText(ref reader);
            case JsonTokenType.String:
                var s = reader.GetString()!.Trim();
                return s.Length == 0 ? null : s;
            default:
                throw JsonConfig.CannotDeserialize(javaType, ref reader);
        }
    }

    public static long ParseIntegral(string text, string javaType, long min, long max)
    {
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l) && l >= min && l <= max)
            return l;
        // ACCEPT_FLOAT_AS_INT: 2.0 / 2.7 -> truncated
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= min && d <= max)
            return (long)Math.Truncate(d);
        throw new JsonException($"Cannot deserialize value of type `{javaType}` from String \"{text}\": not a valid `{javaType}` value");
    }
}

public sealed class LenientInt32Converter : JsonConverter<int>
{
    public override bool HandleNull => true;
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = Lenient.ScalarText(ref reader, "int");
        return text is null ? 0 : (int)Lenient.ParseIntegral(text, "int", int.MinValue, int.MaxValue);
    }
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

public sealed class LenientNullableInt32Converter : JsonConverter<int?>
{
    public override bool HandleNull => true;
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = Lenient.ScalarText(ref reader, "java.lang.Integer");
        return text is null ? null : (int)Lenient.ParseIntegral(text, "java.lang.Integer", int.MinValue, int.MaxValue);
    }
    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteNumberValue(value.Value);
    }
}

public sealed class LenientInt64Converter : JsonConverter<long>
{
    public override bool HandleNull => true;
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = Lenient.ScalarText(ref reader, "long");
        return text is null ? 0 : Lenient.ParseIntegral(text, "long", long.MinValue, long.MaxValue);
    }
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

public sealed class LenientNullableInt64Converter : JsonConverter<long?>
{
    public override bool HandleNull => true;
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = Lenient.ScalarText(ref reader, "java.lang.Long");
        return text is null ? null : Lenient.ParseIntegral(text, "java.lang.Long", long.MinValue, long.MaxValue);
    }
    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteNumberValue(value.Value);
    }
}

/// <summary>Writes doubles exactly as Jackson does (Double#toString: 90000.0, 1.0E7).</summary>
public sealed class JavaDoubleConverter : JsonConverter<double>
{
    public override bool HandleNull => true;
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = Lenient.ScalarText(ref reader, "double");
        return text is null ? 0 : ParseDouble(text, "double");
    }
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
        writer.WriteRawValue(JavaFormat.Double(value), skipInputValidation: true);

    internal static double ParseDouble(string text, string javaType) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d
            : throw new JsonException($"Cannot deserialize value of type `{javaType}` from String \"{text}\": not a valid `{javaType}` value");
}

public sealed class JavaNullableDoubleConverter : JsonConverter<double?>
{
    public override bool HandleNull => true;
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = Lenient.ScalarText(ref reader, "java.lang.Double");
        return text is null ? null : JavaDoubleConverter.ParseDouble(text, "java.lang.Double");
    }
    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteRawValue(JavaFormat.Double(value.Value), skipInputValidation: true);
    }
}

/// <summary>BigDecimal: keeps the scale of the incoming number ("45000" stays "45000", "45000.00" stays "45000.00").</summary>
public sealed class LenientDecimalConverter : JsonConverter<decimal>
{
    public override bool HandleNull => true;
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = Lenient.ScalarText(ref reader, "java.math.BigDecimal");
        return text is null ? 0 : ParseDecimal(text);
    }
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);

    internal static decimal ParseDecimal(string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d
            : throw new JsonException($"Cannot deserialize value of type `java.math.BigDecimal` from String \"{text}\": not a valid representation");
}

public sealed class LenientNullableDecimalConverter : JsonConverter<decimal?>
{
    public override bool HandleNull => true;
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = Lenient.ScalarText(ref reader, "java.math.BigDecimal");
        return text is null ? null : LenientDecimalConverter.ParseDecimal(text);
    }
    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteNumberValue(value.Value);
    }
}

// ---------------------------------------------------------------- booleans / strings

internal static class LenientBool
{
    public static bool? Read(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: return null;
            case JsonTokenType.True: return true;
            case JsonTokenType.False: return false;
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var l) ? l != 0 : throw JsonConfig.CannotDeserialize("java.lang.Boolean", ref reader);
            case JsonTokenType.String:
                var s = reader.GetString()!.Trim();
                if (s.Length == 0) return null;
                if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                throw new JsonException($"Cannot deserialize value of type `java.lang.Boolean` from String \"{s}\": only \"true\" or \"false\" recognized");
            default:
                throw JsonConfig.CannotDeserialize("java.lang.Boolean", ref reader);
        }
    }
}

public sealed class LenientBooleanConverter : JsonConverter<bool>
{
    public override bool HandleNull => true;
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        LenientBool.Read(ref reader) ?? false;
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}

public sealed class LenientNullableBooleanConverter : JsonConverter<bool?>
{
    public override bool HandleNull => true;
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        LenientBool.Read(ref reader);
    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteBooleanValue(value.Value);
    }
}

/// <summary>Jackson coerces scalars into String fields (123 -> "123", true -> "true").</summary>
public sealed class LenientStringConverter : JsonConverter<string>
{
    public override bool HandleNull => true;
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => JsonConfig.TokenText(ref reader),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => throw JsonConfig.CannotDeserialize("java.lang.String", ref reader),
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteStringValue(value);
    }
}

// ---------------------------------------------------------------- dates

/// <summary>LocalDateTime as Jackson's ISO_LOCAL_DATE_TIME (no offset, seconds always, trimmed fraction).</summary>
public sealed class JacksonLocalDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(JavaFormat.IsoLocalDateTime(value));
}
