using System.Text.Json.Serialization;

namespace Celebria.Models;

/// <summary>
/// Query Object: заявка за търсене на събития, която Gemini връща от текст на естествен език.
/// Съдържа само логически полета и абстрактни оператори – никакъв SQL. Преводът до SQL прави
/// <see cref="Services.JsonQuerySqlTranslator"/>, който проверява всичко по бял списък.
/// </summary>
public sealed class JSONQuery
{
    /// <summary>Как се свързват условията: "AND" (по подразбиране) или "OR".</summary>
    [JsonPropertyName("logicalOperator")]
    public string? LogicalOperator { get; set; } = "AND";

    [JsonPropertyName("conditions")]
    public List<QueryCondition>? Conditions { get; set; } = new();

    /// <summary>Поле за сортиране (по подразбиране Date).</summary>
    [JsonPropertyName("orderBy")]
    public string? OrderBy { get; set; } = "Date";

    /// <summary>"ASC" или "DESC".</summary>
    [JsonPropertyName("sortDirection")]
    public string? SortDirection { get; set; } = "ASC";

    /// <summary>Максимален брой резултати; null = без ограничение.</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    /// <summary>Кратко обяснение на български как моделът е разбрал заявката (показва се на потребителя).</summary>
    [JsonPropertyName("explanation")]
    public string? Explanation { get; set; }
}

/// <summary>Едно условие: поле, оператор и стойност. Стойността винаги стига до SQL само като параметър.</summary>
public sealed class QueryCondition
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("operator")]
    public string? Operator { get; set; } = "contains";

    /// <summary>Приема и текст, и число ("100" или 100) – моделът понякога връща числата без кавички.</summary>
    [JsonPropertyName("value")]
    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string? Value { get; set; }
}

/// <summary>Чете JSON стойност (текст, число или true/false) като низ.</summary>
public sealed class FlexibleStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            System.Text.Json.JsonTokenType.String => reader.GetString(),
            System.Text.Json.JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(reader.ValueSpan),
            System.Text.Json.JsonTokenType.True => "true",
            System.Text.Json.JsonTokenType.False => "false",
            System.Text.Json.JsonTokenType.Null => null,
            _ => throw new System.Text.Json.JsonException("Стойността на условието трябва да е текст или число.")
        };

    public override void Write(System.Text.Json.Utf8JsonWriter writer, string? value, System.Text.Json.JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
