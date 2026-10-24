using System.Globalization;
using Celebria.Data;
using Celebria.Models;

namespace Celebria.Services;

/// <summary>
/// Параметризирана SQL SELECT заявка, получена от <see cref="JsonQuerySqlTranslator"/>.
/// Конструкторът е internal – заявка може да създаде само преводачът, а не произволен код или ИИ модел.
/// </summary>
public sealed class SqlSelectCommand
{
    internal SqlSelectCommand(string sql, IReadOnlyDictionary<string, object> parameters, string readableSql)
    {
        Sql = sql;
        Parameters = parameters;
        ReadableSql = readableSql;
    }

    /// <summary>SQL с параметри ($p0, $p1, ...) – това се изпълнява.</summary>
    public string Sql { get; }

    public IReadOnlyDictionary<string, object> Parameters { get; }

    /// <summary>Същата заявка със стойностите вместо параметрите – само за показване в интерфейса.</summary>
    public string ReadableSql { get; }
}

/// <summary>
/// Query Object + Interpreter: превежда <see cref="JSONQuery"/> до параметризирана SELECT заявка върху Events.
///  - полетата и операторите се проверяват по бял списък (whitelist); всичко друго е грешка;
///  - стойностите стигат до SQL само като параметри – SQL инжекция е невъзможна;
///  - служебните колони (SearchKey, OwnerId, CreatedAt) не са в белия списък и не могат да се търсят;
///  - текстът се сравнява без значение от регистъра и на кирилица (LOWER е заменена с Unicode версия в Database).
/// Хвърля <see cref="ArgumentException"/> при невалидна заявка – контролерът я показва като съобщение.
/// </summary>
public static class JsonQuerySqlTranslator
{
    public const int MaxConditions = 10;
    public const int MaxLimit = 100;

    private const string DateFormat = "yyyy-MM-dd";
    private static readonly string BaseSelect = $"SELECT {Database.EventColumns} FROM Events";

    private enum FieldKind { Text, NullableText, Number, Date, EventType, Status }

    private sealed record FieldSpec(string Column, FieldKind Kind);

    /// <summary>Бял списък: логическо поле (и синоними на български) → физическа колона и вид.</summary>
    private static readonly IReadOnlyDictionary<string, FieldSpec> Fields = BuildFields();

    private static IReadOnlyDictionary<string, FieldSpec> BuildFields()
    {
        var map = new Dictionary<string, FieldSpec>(StringComparer.OrdinalIgnoreCase);
        void Add(FieldSpec spec, params string[] names)
        {
            foreach (var name in names)
            {
                map[name] = spec;
            }
        }

        Add(new("Title", FieldKind.Text), "Title", "наименование", "име", "заглавие", "събитие");
        Add(new("ClientName", FieldKind.Text), "ClientName", "Client", "клиент");
        Add(new("Location", FieldKind.NullableText), "Location", "място", "локация", "град");
        Add(new("EventType", FieldKind.EventType), "EventType", "Type", "тип");
        Add(new("Status", FieldKind.Status), "Status", "статус");
        Add(new("Date", FieldKind.Date), "Date", "дата");
        Add(new("GuestCount", FieldKind.Number), "GuestCount", "Guests", "гости", "брой гости");
        Add(new("BudgetEur", FieldKind.Number), "BudgetEur", "Budget", "бюджет");
        return map;
    }

    /// <summary>Логическите полета, които могат да се ползват (за системния промпт и документацията).</summary>
    public static IReadOnlyList<string> PublicFields { get; } =
        new[] { "Title", "ClientName", "Location", "EventType", "Status", "Date", "GuestCount", "BudgetEur" };

    public static SqlSelectCommand Translate(JSONQuery query, EventTypeCatalog? types = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        types ??= EventTypeCatalog.Instance;

        var logical = NormalizeLogicalOperator(query.LogicalOperator);
        var conditions = (query.Conditions ?? new List<QueryCondition>())
            .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Field) && !string.IsNullOrWhiteSpace(c.Value))
            .ToList();
        if (conditions.Count > MaxConditions)
        {
            throw new ArgumentException($"Заявката има твърде много условия (максимум {MaxConditions}).");
        }

        var where = new List<string>();
        var readableWhere = new List<string>();
        var parameters = new Dictionary<string, object>();

        for (var i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            var spec = ResolveField(condition.Field!);
            var op = NormalizeOperator(condition.Operator);
            var param = $"$p{i}";
            var (clause, readable, value) = spec.Kind switch
            {
                FieldKind.Text or FieldKind.NullableText => TextCondition(spec, op, condition.Value!.Trim(), param),
                FieldKind.Number => NumberCondition(spec, op, condition.Value!.Trim(), param),
                FieldKind.Date => DateCondition(spec, op, condition.Value!.Trim(), param),
                FieldKind.EventType => ExactCondition(spec, op, ResolveEventType(condition.Value!, types), param),
                FieldKind.Status => ExactCondition(spec, op, ResolveStatus(condition.Value!), param),
                _ => throw new ArgumentException($"Неподдържано поле: '{condition.Field}'.")
            };
            where.Add(clause);
            readableWhere.Add(readable);
            parameters[param] = value;
        }

        var orderColumn = string.IsNullOrWhiteSpace(query.OrderBy) ? "Date" : ResolveField(query.OrderBy).Column;
        var direction = NormalizeSortDirection(query.SortDirection);
        // Втори ключ за сортиране – стабилен ред при еднакви стойности.
        var orderSql = orderColumn == "Title"
            ? $" ORDER BY Title {direction}"
            : $" ORDER BY {orderColumn} {direction}, Title ASC";

        var whereSql = where.Count > 0 ? " WHERE " + string.Join($" {logical} ", where) : string.Empty;
        var readableWhereSql = readableWhere.Count > 0 ? " WHERE " + string.Join($" {logical} ", readableWhere) : string.Empty;

        var limitSql = string.Empty;
        var readableLimitSql = string.Empty;
        if (query.Limit is > 0)
        {
            var limit = Math.Min(query.Limit.Value, MaxLimit);
            limitSql = " LIMIT $limit";
            readableLimitSql = $" LIMIT {limit}";
            parameters["$limit"] = limit;
        }

        return new SqlSelectCommand(
            BaseSelect + whereSql + orderSql + limitSql,
            parameters,
            BaseSelect + readableWhereSql + orderSql + readableLimitSql);
    }

    // ---------------------------------------------------------------- условия по вид поле

    private static (string, string, object) TextCondition(FieldSpec spec, string op, string raw, string param)
    {
        var lower = raw.ToLowerInvariant();
        var col = spec.Column;
        return op switch
        {
            "contains" => ($@"LOWER({col}) LIKE {param} ESCAPE '\'", $"LOWER({col}) LIKE {Quote($"%{lower}%")}", $"%{EscapeLike(lower)}%"),
            "startswith" => ($@"LOWER({col}) LIKE {param} ESCAPE '\'", $"LOWER({col}) LIKE {Quote($"{lower}%")}", $"{EscapeLike(lower)}%"),
            "endswith" => ($@"LOWER({col}) LIKE {param} ESCAPE '\'", $"LOWER({col}) LIKE {Quote($"%{lower}")}", $"%{EscapeLike(lower)}"),
            "equals" => ($"LOWER({col}) = {param}", $"LOWER({col}) = {Quote(lower)}", lower),
            "notequals" => ($"({col} IS NULL OR LOWER({col}) <> {param})", $"({col} IS NULL OR LOWER({col}) <> {Quote(lower)})", lower),
            _ => throw new ArgumentException($"Операторът '{op}' не е позволен за текстовото поле {col}.")
        };
    }

    private static (string, string, object) NumberCondition(FieldSpec spec, string op, string raw, string param)
    {
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 0 || number > 10_000_000)
        {
            throw new ArgumentException($"Стойността за {spec.Column} трябва да е цяло число между 0 и 10 000 000, а е '{raw}'.");
        }
        var sqlOp = ComparisonOperator(op, spec.Column);
        return ($"{spec.Column} {sqlOp} {param}", $"{spec.Column} {sqlOp} {number}", number);
    }

    private static (string, string, object) DateCondition(FieldSpec spec, string op, string raw, string param)
    {
        if (!DateTime.TryParseExact(raw, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new ArgumentException($"Датата трябва да е във формат {DateFormat}, а е '{raw}'.");
        }
        var sqlOp = ComparisonOperator(op, spec.Column);
        var text = date.ToString(DateFormat, CultureInfo.InvariantCulture);
        return ($"{spec.Column} {sqlOp} {param}", $"{spec.Column} {sqlOp} {Quote(text)}", text);
    }

    /// <summary>Тип и статус: само точно съвпадение (или различно от) с вече проверена стойност.</summary>
    private static (string, string, object) ExactCondition(FieldSpec spec, string op, string value, string param) => op switch
    {
        "equals" or "contains" => ($"{spec.Column} = {param}", $"{spec.Column} = {Quote(value)}", value),
        "notequals" => ($"{spec.Column} <> {param}", $"{spec.Column} <> {Quote(value)}", value),
        _ => throw new ArgumentException($"Операторът '{op}' не е позволен за полето {spec.Column}.")
    };

    private static string ComparisonOperator(string op, string column) => op switch
    {
        "equals" => "=",
        "notequals" => "<>",
        "gt" => ">",
        "gte" => ">=",
        "lt" => "<",
        "lte" => "<=",
        _ => throw new ArgumentException($"Операторът '{op}' не е позволен за полето {column}.")
    };

    // ---------------------------------------------------------------- валидиране на стойности

    private static string ResolveEventType(string value, EventTypeCatalog types)
    {
        var trimmed = value.Trim();
        var match = types.Find(trimmed)
            ?? types.All.FirstOrDefault(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        return match?.Code ?? throw new ArgumentException($"Непознат тип събитие: '{value}'.");
    }

    private static string ResolveStatus(string value)
    {
        var trimmed = value.Trim();
        foreach (var status in Enum.GetValues<EventStatus>())
        {
            if (string.Equals(status.ToString(), trimmed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status.DisplayName(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return status.ToString();
            }
        }
        throw new ArgumentException($"Непознат статус: '{value}'.");
    }

    private static FieldSpec ResolveField(string field) =>
        Fields.TryGetValue(field.Trim(), out var spec)
            ? spec
            : throw new ArgumentException($"Полето '{field}' не е позволено за търсене.");

    private static string NormalizeOperator(string? op) => (op ?? "contains").Trim().ToLowerInvariant() switch
    {
        "contains" or "like" => "contains",
        "equals" or "eq" or "=" or "==" => "equals",
        "notequals" or "neq" or "!=" or "<>" => "notequals",
        "startswith" => "startswith",
        "endswith" => "endswith",
        "gt" or ">" => "gt",
        "gte" or ">=" => "gte",
        "lt" or "<" => "lt",
        "lte" or "<=" => "lte",
        var other => throw new ArgumentException($"Неподдържан оператор: '{other}'.")
    };

    private static string NormalizeLogicalOperator(string? value) => (value ?? "AND").Trim().ToUpperInvariant() switch
    {
        "" or "AND" or "И" => "AND",
        "OR" or "ИЛИ" => "OR",
        _ => throw new ArgumentException($"Невалиден логически оператор: '{value}'. Позволени са само AND и OR.")
    };

    private static string NormalizeSortDirection(string? value) => (value ?? "ASC").Trim().ToUpperInvariant() switch
    {
        "" or "ASC" or "ASCENDING" => "ASC",
        "DESC" or "DESCENDING" => "DESC",
        _ => throw new ArgumentException($"Невалидна посока на сортиране: '{value}'. Позволени са само ASC и DESC.")
    };

    /// <summary>% и _ от потребителя се търсят буквално, а не като шаблони на LIKE.</summary>
    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    /// <summary>Само за четимия SQL в интерфейса – истинската заявка е с параметри.</summary>
    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
