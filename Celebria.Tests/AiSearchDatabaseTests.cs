using Microsoft.Data.Sqlite;
using Celebria.Data;
using Celebria.Models;
using Celebria.Services;
using static Celebria.Tests.Q;

namespace Celebria.Tests;

/// <summary>
/// Целият път JSONQuery → JsonQuerySqlTranslator → Database.ExecuteSqlSelect върху истинска SQLite база
/// във временен файл (отделна за всеки тест).
/// </summary>
public sealed class AiSearchDatabaseTests : IDisposable
{
    private readonly string _file;
    private readonly Database _db;

    public AiSearchDatabaseTests()
    {
        _file = Path.Combine(Path.GetTempPath(), $"celebria-ai-{Guid.NewGuid():N}.db");
        _db = Database.CreateIsolated($"Data Source={_file}");

        Add("Сватба на Мария и Иван", "wedding", "2026-11-14", 120, "Мария Георгиева", "Хотел „Империал“, Пловдив", 18000, EventStatus.Confirmed);
        Add("Кръщене на София", "baptism", "2026-11-22", 45, "Елена Николова", "Ресторант „Хемингуей“, Пловдив", 3500, EventStatus.Planned);
        Add("Сватба на Анна и Петър", "wedding", "2026-12-12", 180, "Анна Стефанова", "Комплекс „Старосел“", 26000, EventStatus.Planned);
        Add("Коледно парти на Musala Soft", "corporate", "2026-12-20", 250, "Musala Soft", "Интер Експо Център, София", 32000, EventStatus.Confirmed);
        Add("Тиймбилдинг на Chaos", "corporate", "2026-09-10", 80, "Chaos", null, null, EventStatus.Completed);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }

    private void Add(string title, string type, string date, int guests, string client, string? location, int? budget, EventStatus status) =>
        _db.Add(new Event
        {
            Title = title, EventType = type, Date = DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
            GuestCount = guests, ClientName = client, Location = location, BudgetEur = budget, Status = status
        });

    private IReadOnlyList<Event> Run(JSONQuery query) => _db.ExecuteSqlSelect(JsonQuerySqlTranslator.Translate(query));

    [Fact]
    public void CyrillicContains_IgnoresCase()
    {
        var result = Run(Of(C("Location", "contains", "ПЛОВДИВ")));

        Assert.Equal(new[] { "Сватба на Мария и Иван", "Кръщене на София" }, result.Select(e => e.Title));
    }

    [Fact]
    public void CyrillicEquals_IgnoresCase()
    {
        Assert.Single(Run(Of(C("ClientName", "equals", "мария георгиева"))));
    }

    [Fact]
    public void TypeAndStatus_CombineWithAnd()
    {
        var result = Run(Of(C("EventType", "equals", "Сватба"), C("Status", "equals", "Confirmed")));

        Assert.Equal("Сватба на Мария и Иван", Assert.Single(result).Title);
    }

    [Fact]
    public void Or_ReturnsEitherCondition()
    {
        var query = Of(C("EventType", "equals", "baptism"), C("EventType", "equals", "corporate"));
        query.LogicalOperator = "OR";

        Assert.Equal(3, Run(query).Count);
    }

    [Fact]
    public void DateRange_IsInclusive()
    {
        var result = Run(Of(C("Date", "gte", "2026-12-01"), C("Date", "lte", "2026-12-20")));

        Assert.Equal(new[] { "Сватба на Анна и Петър", "Коледно парти на Musala Soft" }, result.Select(e => e.Title));
    }

    [Fact]
    public void NumberComparison_AndNullBudgetIsIgnored()
    {
        Assert.Equal(3, Run(Of(C("BudgetEur", "gt", "10000"))).Count);
        Assert.Equal(2, Run(Of(C("GuestCount", "lte", "80"))).Count);
    }

    [Fact]
    public void NotEqualsOnLocation_IncludesEventsWithoutLocation()
    {
        var result = Run(Of(C("Location", "notEquals", "Комплекс „Старосел“")));

        Assert.Equal(4, result.Count);
        Assert.Contains(result, e => e.Location is null);
    }

    [Fact]
    public void OrderAndLimit_ReturnTopByGuests()
    {
        var result = Run(new JSONQuery { OrderBy = "GuestCount", SortDirection = "DESC", Limit = 2 });

        Assert.Equal(new[] { 250, 180 }, result.Select(e => e.GuestCount!.Value));
    }

    [Fact]
    public void InjectionAttemptInValue_FindsNothing_AndTableIsIntact()
    {
        var result = Run(Of(C("Title", "contains", "'; DROP TABLE Events; --")));

        Assert.Empty(result);
        Assert.Equal(5, _db.Count());
    }

    [Fact]
    public void PercentFromUser_IsLiteral_NotWildcard()
    {
        Assert.Empty(Run(Of(C("Title", "contains", "%"))));
    }

    [Fact]
    public void ResultEvents_AreFullyMapped()
    {
        var ev = Assert.Single(Run(Of(C("Title", "equals", "Тиймбилдинг на Chaos"))));

        Assert.Equal("corporate", ev.EventType);
        Assert.Equal(new DateTime(2026, 9, 10), ev.Date);
        Assert.Equal(EventStatus.Completed, ev.Status);
        Assert.Null(ev.Location);
        Assert.Null(ev.BudgetEur);
    }

    [Fact]
    public void ExistingFilterSearch_StillWorks_AfterLowerOverride()
    {
        Assert.Equal(2, _db.Search(new EventFilter { Search = "пловдив" }).Count);
    }
}
