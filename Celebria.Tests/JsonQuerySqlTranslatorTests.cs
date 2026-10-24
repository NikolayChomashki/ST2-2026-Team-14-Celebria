using Celebria.Models;
using Celebria.Services;
using static Celebria.Tests.Q;

namespace Celebria.Tests;

/// <summary>
/// Преводачът JSONQuery → SQL е бариерата за сигурност: всичко от модела минава оттук.
/// Тестовете са бързи и детерминистични – без Gemini и без база.
/// </summary>
public class JsonQuerySqlTranslatorTests
{
    private const string Select = "SELECT Id, Title, EventType, Date, GuestCount, ClientName, Location, BudgetEur, Status, Notes, OwnerId FROM Events";

    // ------------------------------------------------------------ основни случаи

    [Fact]
    public void EmptyQuery_SelectsAll_OrderedByDate()
    {
        var cmd = JsonQuerySqlTranslator.Translate(new JSONQuery());

        Assert.Equal(Select + " ORDER BY Date ASC, Title ASC", cmd.Sql);
        Assert.Empty(cmd.Parameters);
    }

    [Fact]
    public void TextContains_UsesLowerLikeWithParameter_AndLowercasedValue()
    {
        var cmd = JsonQuerySqlTranslator.Translate(Of(C("Location", "contains", "Пловдив")));

        Assert.Contains(@"LOWER(Location) LIKE $p0 ESCAPE '\'", cmd.Sql);
        Assert.Equal("%пловдив%", cmd.Parameters["$p0"]);
        Assert.DoesNotContain("пловдив", cmd.Sql, StringComparison.OrdinalIgnoreCase);   // стойността е само параметър
    }

    [Theory]
    [InlineData("startsWith", "сватба%")]
    [InlineData("endsWith", "%сватба")]
    [InlineData("equals", "сватба")]
    public void TextOperators_BuildExpectedParameter(string op, string expected)
    {
        var cmd = JsonQuerySqlTranslator.Translate(Of(C("Title", op, "Сватба")));

        Assert.Equal(expected, cmd.Parameters["$p0"]);
    }

    [Fact]
    public void TextNotEquals_IncludesNullRows()
    {
        var cmd = JsonQuerySqlTranslator.Translate(Of(C("Location", "notEquals", "Банско")));

        Assert.Contains("(Location IS NULL OR LOWER(Location) <> $p0)", cmd.Sql);
    }

    [Fact]
    public void LikeWildcardsFromUser_AreEscaped()
    {
        var cmd = JsonQuerySqlTranslator.Translate(Of(C("Title", "contains", "100%_off")));

        Assert.Equal(@"%100\%\_off%", cmd.Parameters["$p0"]);
    }

    [Fact]
    public void BulgarianFieldSynonyms_AreAccepted()
    {
        var cmd = JsonQuerySqlTranslator.Translate(Of(C("клиент", "contains", "Мария"), C("гости", "gt", "50")));

        Assert.Contains("LOWER(ClientName) LIKE $p0", cmd.Sql);
        Assert.Contains("GuestCount > $p1", cmd.Sql);
    }

    // ------------------------------------------------------------ числа и дати

    [Theory]
    [InlineData("gt", ">")]
    [InlineData("gte", ">=")]
    [InlineData("lt", "<")]
    [InlineData("lte", "<=")]
    [InlineData("equals", "=")]
    [InlineData("notEquals", "<>")]
    public void NumberOperators_MapToSql(string op, string sql)
    {
        var cmd = JsonQuerySqlTranslator.Translate(Of(C("GuestCount", op, "100")));

        Assert.Contains($"GuestCount {sql} $p0", cmd.Sql);
        Assert.Equal(100, cmd.Parameters["$p0"]);
    }

    [Theory]
    [InlineData("сто")]
    [InlineData("-5")]
    [InlineData("10.5")]
    [InlineData("99999999999")]
    public void InvalidNumber_IsRejected(string value)
    {
        Assert.Throws<ArgumentException>(() => JsonQuerySqlTranslator.Translate(Of(C("BudgetEur", "gt", value))));
    }

    [Fact]
    public void DateRange_BecomesTwoParameterizedConditions()
    {
        var cmd = JsonQuerySqlTranslator.Translate(Of(C("Date", "gte", "2026-12-01"), C("Date", "lte", "2026-12-31")));

        Assert.Contains("Date >= $p0 AND Date <= $p1", cmd.Sql);
        Assert.Equal("2026-12-01", cmd.Parameters["$p0"]);
        Assert.Equal("2026-12-31", cmd.Parameters["$p1"]);
    }

    [Theory]
    [InlineData("01.12.2026")]
    [InlineData("утре")]
    [InlineData("2026-13-40")]
    public void InvalidDate_IsRejected(string value)
    {
        Assert.Throws<ArgumentException>(() => JsonQuerySqlTranslator.Translate(Of(C("Date", "gte", value))));
    }

    [Fact]
    public void ContainsOnNumber_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => JsonQuerySqlTranslator.Translate(Of(C("GuestCount", "contains", "1"))));
    }

    // ------------------------------------------------------------ тип и статус

    [Theory]
    [InlineData("wedding")]
    [InlineData("WEDDING")]
    [InlineData("Сватба")]
    public void EventType_AcceptsCodeOrBulgarianName_AndStoresCode(string value)
    {
        var cmd = JsonQuerySqlTranslator.Translate(Of(C("EventType", "equals", value)));

        Assert.Contains("EventType = $p0", cmd.Sql);
        Assert.Equal("wedding", cmd.Parameters["$p0"]);
    }

    [Fact]
    public void UnknownEventType_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => JsonQuerySqlTranslator.Translate(Of(C("EventType", "equals", "funeral"))));
    }

    [Theory]
    [InlineData("Confirmed")]
    [InlineData("потвърдено")]
    public void Status_AcceptsEnumOrBulgarianName(string value)
    {
        var cmd = JsonQuerySqlTranslator.Translate(Of(C("Status", "equals", value)));

        Assert.Equal("Confirmed", cmd.Parameters["$p0"]);
    }

    [Fact]
    public void UnknownStatus_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => JsonQuerySqlTranslator.Translate(Of(C("Status", "equals", "Archived"))));
    }

    // ------------------------------------------------------------ сигурност: бял списък

    [Theory]
    [InlineData("SearchKey")]
    [InlineData("OwnerId")]
    [InlineData("CreatedAt")]
    [InlineData("PasswordHash")]
    [InlineData("sqlite_master")]
    [InlineData("Title; DROP TABLE Events")]
    public void FieldsOutsideWhitelist_AreRejected(string field)
    {
        var ex = Assert.Throws<ArgumentException>(() => JsonQuerySqlTranslator.Translate(Of(C(field, "equals", "x"))));
        Assert.Contains("не е позволено", ex.Message);
    }

    [Theory]
    [InlineData("UNION")]
    [InlineData("; DROP TABLE Events")]
    [InlineData("regex")]
    public void UnknownOperators_AreRejected(string op)
    {
        Assert.Throws<ArgumentException>(() => JsonQuerySqlTranslator.Translate(Of(C("Title", op, "x"))));
    }

    [Fact]
    public void SqlInjectionInValue_StaysInsideParameter()
    {
        const string attack = "x'); DROP TABLE Events; --";

        var cmd = JsonQuerySqlTranslator.Translate(Of(C("Title", "contains", attack)));

        Assert.DoesNotContain("DROP", cmd.Sql);
        Assert.Contains("drop table events", (string)cmd.Parameters["$p0"]);
    }

    [Fact]
    public void ReadableSql_EscapesQuotes()
    {
        var cmd = JsonQuerySqlTranslator.Translate(Of(C("ClientName", "equals", "O'Brien")));

        Assert.Contains("'o''brien'", cmd.ReadableSql);
    }

    [Theory]
    [InlineData("XOR")]
    [InlineData("AND 1=1")]
    public void InvalidLogicalOperator_IsRejected(string op)
    {
        var query = Of(C("Title", "contains", "a"));
        query.LogicalOperator = op;

        Assert.Throws<ArgumentException>(() => JsonQuerySqlTranslator.Translate(query));
    }

    [Fact]
    public void OrOperator_JoinsConditionsWithOr()
    {
        var query = Of(C("EventType", "equals", "wedding"), C("EventType", "equals", "baptism"));
        query.LogicalOperator = "или";

        Assert.Contains("EventType = $p0 OR EventType = $p1", JsonQuerySqlTranslator.Translate(query).Sql);
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("SearchKey")]
    [InlineData("Date; DROP TABLE Events")]
    public void OrderByOutsideWhitelist_IsRejected(string orderBy)
    {
        Assert.Throws<ArgumentException>(() => JsonQuerySqlTranslator.Translate(new JSONQuery { OrderBy = orderBy }));
    }

    [Fact]
    public void InvalidSortDirection_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => JsonQuerySqlTranslator.Translate(new JSONQuery { SortDirection = "DESC; --" }));
    }

    // ------------------------------------------------------------ сортиране, лимит, граници

    [Fact]
    public void OrderAndLimit_AreApplied_LimitIsParameterAndClamped()
    {
        var cmd = JsonQuerySqlTranslator.Translate(new JSONQuery { OrderBy = "GuestCount", SortDirection = "desc", Limit = 5000 });

        Assert.EndsWith(" ORDER BY GuestCount DESC, Title ASC LIMIT $limit", cmd.Sql);
        Assert.Equal(JsonQuerySqlTranslator.MaxLimit, cmd.Parameters["$limit"]);
    }

    [Fact]
    public void ZeroOrNegativeLimit_MeansNoLimit()
    {
        Assert.DoesNotContain("LIMIT", JsonQuerySqlTranslator.Translate(new JSONQuery { Limit = 0 }).Sql);
    }

    [Fact]
    public void TooManyConditions_AreRejected()
    {
        var conditions = Enumerable.Range(0, JsonQuerySqlTranslator.MaxConditions + 1)
            .Select(i => C("Title", "contains", i.ToString())).ToArray();

        Assert.Throws<ArgumentException>(() => JsonQuerySqlTranslator.Translate(Of(conditions)));
    }

    [Fact]
    public void EmptyOrIncompleteConditions_AreSkipped()
    {
        var query = new JSONQuery { Conditions = new List<QueryCondition> { null!, new() { Field = "Title", Value = "" }, new() { Field = "", Value = "x" } } };

        Assert.Empty(JsonQuerySqlTranslator.Translate(query).Parameters);
    }

    [Fact]
    public void NullConditionsList_IsTreatedAsEmpty()
    {
        Assert.Empty(JsonQuerySqlTranslator.Translate(new JSONQuery { Conditions = null }).Parameters);
    }
}
