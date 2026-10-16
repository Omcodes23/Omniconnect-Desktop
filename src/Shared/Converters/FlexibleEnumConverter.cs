using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniConnect.Shared.Converters;

public class FlexibleEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        var underlying = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        return underlying.IsEnum;
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var isNullable = Nullable.GetUnderlyingType(typeToConvert) != null;
        var enumType = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;

        var converterType = isNullable
            ? typeof(NullableFlexibleEnumConverter<>).MakeGenericType(enumType)
            : typeof(FlexibleEnumConverter<>).MakeGenericType(enumType);

        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}

public class FlexibleEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            var intVal = reader.GetInt32();
            return (TEnum)(object)intVal;
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            var strVal = reader.GetString();
            if (!string.IsNullOrWhiteSpace(strVal))
            {
                if (Enum.TryParse<TEnum>(strVal, ignoreCase: true, out var parsedEnum))
                    return parsedEnum;

                if (int.TryParse(strVal, out var parsedInt))
                    return (TEnum)(object)parsedInt;

                if (strVal.Contains(','))
                {
                    int combined = 0;
                    foreach (var part in strVal.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (Enum.TryParse<TEnum>(part, ignoreCase: true, out var partEnum))
                            combined |= Convert.ToInt32(partEnum);
                    }
                    return (TEnum)(object)combined;
                }
            }
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            int combined = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    var itemStr = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(itemStr) && Enum.TryParse<TEnum>(itemStr, ignoreCase: true, out var parsedEnum))
                    {
                        combined |= Convert.ToInt32(parsedEnum);
                    }
                }
                else if (reader.TokenType == JsonTokenType.Number)
                {
                    combined |= reader.GetInt32();
                }
            }
            return (TEnum)(object)combined;
        }

        return default;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

public class NullableFlexibleEnumConverter<TEnum> : JsonConverter<TEnum?> where TEnum : struct, Enum
{
    public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.Number)
        {
            var intVal = reader.GetInt32();
            return (TEnum)(object)intVal;
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            var strVal = reader.GetString();
            if (!string.IsNullOrWhiteSpace(strVal))
            {
                if (Enum.TryParse<TEnum>(strVal, ignoreCase: true, out var parsedEnum))
                    return parsedEnum;

                if (int.TryParse(strVal, out var parsedInt))
                    return (TEnum)(object)parsedInt;

                if (strVal.Contains(','))
                {
                    int combined = 0;
                    foreach (var part in strVal.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (Enum.TryParse<TEnum>(part, ignoreCase: true, out var partEnum))
                            combined |= Convert.ToInt32(partEnum);
                    }
                    return (TEnum)(object)combined;
                }
            }
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            int combined = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    var itemStr = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(itemStr) && Enum.TryParse<TEnum>(itemStr, ignoreCase: true, out var parsedEnum))
                    {
                        combined |= Convert.ToInt32(parsedEnum);
                    }
                }
                else if (reader.TokenType == JsonTokenType.Number)
                {
                    combined |= reader.GetInt32();
                }
            }
            return (TEnum)(object)combined;
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToString());
        else
            writer.WriteNullValue();
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static JsonSerializerOptions CreateOptions()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        opts.Converters.Add(new FlexibleEnumConverterFactory());
        return opts;
    }
}
