using Microsoft.Data.Sqlite;
using Celebria.Data;
using Celebria.Models;

namespace Celebria.Tests;

/// <summary>
/// Интеграционни тестове за INSERT и SELECT с филтър върху истинска SQLite база
/// във временен файл. Всеки тест получава собствена празна база (xUnit създава нов
/// обект на класа за всеки тест), затова тестовете не си пречат.
/// </summary>
public sealed class DatabaseTests : IDisposable
{
    private readonly string _file;
    private readonly Database _db;

    public DatabaseTests()
    {
        _file = Path.Combine(Path.GetTempPath(), $"celebria-test-{Guid.NewGuid():N}.db");
        _db = Database.CreateIsolated($"Data Source={_file}");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();   // освобождава файла (важно под Windows)
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }

    private static Event Sample(string title = "Сватба на Мария и Иван", string type = "wedding",
                                string date = "2026-11-14", EventStatus status = EventStatus.Planned,
                                string client = "Мария Георгиева", string? location = "Пловдив") => new()
    {
        Title = title,
        EventType = type,
        Date = DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
        GuestCount = 100,
        ClientName = client,
        Location = location,
        BudgetEur = 15000,
        Status = status
    };

    // ------------------------------------------------------------ INSERT

    [Fact]
    public void Add_InsertsRow_AndReturnsNewId()
    {
        var id = _db.Add(Sample());

        Assert.True(id > 0);
        Assert.Equal(1, _db.Count());
    }

    [Fact]
    public void Add_ThenGetById_ReturnsSameData()
    {
        var original = Sample(location: "  Хотел „Империал“  ");
        var id = _db.Add(original);

        var loaded = _db.GetById(id);

        Assert.NotNull(loaded);
        Assert.Equal("Сватба на Мария и Иван", loaded.Title);
        Assert.Equal("wedding", loaded.EventType);
        Assert.Equal(new DateTime(2026, 11, 14), loaded.Date);
        Assert.Equal(100, loaded.GuestCount);
        Assert.Equal("Хотел „Империал“", loaded.Location);   // интервалите са премахнати
        Assert.Equal(15000, loaded.BudgetEur);
        Assert.Equal(EventStatus.Planned, loaded.Status);
    }

    [Fact]
    public void Add_StoresEmptyOptionalFieldsAsNull()
    {
        var ev = Sample(location: "   ");
        ev.BudgetEur = null;
        ev.Notes = "";

        var loaded = _db.GetById(_db.Add(ev))!;

        Assert.Null(loaded.Location);
        Assert.Null(loaded.BudgetEur);
        Assert.Null(loaded.Notes);
    }

    [Fact]
    public void GetById_UnknownId_ReturnsNull()
    {
        Assert.Null(_db.GetById(9999));
    }

    // ------------------------------------------------------------ SELECT с филтър

    private void SeedThree()
    {
        _db.Add(Sample("Сватба на Мария и Иван", "wedding", "2026-11-14", EventStatus.Confirmed));
        _db.Add(Sample("Кръщене на София", "baptism", "2026-11-22", EventStatus.Planned, "Елена Николова", "София"));
        _db.Add(Sample("Сватба на Анна и Петър", "wedding", "2026-12-12", EventStatus.Planned, "Анна Стефанова", "Варна"));
    }

    [Fact]
    public void Search_EmptyFilter_ReturnsAll_OrderedByDate()
    {
        SeedThree();

        var result = _db.Search(new EventFilter());

        Assert.Equal(3, result.Count);
        Assert.Equal(result.OrderBy(e => e.Date).Select(e => e.Id), result.Select(e => e.Id));
    }

    [Fact]
    public void Search_ByType_ReturnsOnlyThatType()
    {
        SeedThree();

        var result = _db.Search(new EventFilter { Type = "wedding" });

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal("wedding", e.EventType));
    }

    [Fact]
    public void Search_ByStatus_ReturnsOnlyThatStatus()
    {
        SeedThree();

        var result = _db.Search(new EventFilter { Status = EventStatus.Confirmed });

        Assert.Single(result);
        Assert.Equal("Сватба на Мария и Иван", result[0].Title);
    }

    [Fact]
    public void Search_ByDateRange_IsInclusive()
    {
        SeedThree();

        var result = _db.Search(new EventFilter { From = new DateTime(2026, 11, 14), To = new DateTime(2026, 11, 22) });

        Assert.Equal(new[] { "Сватба на Мария и Иван", "Кръщене на София" }, result.Select(e => e.Title));
    }

    [Theory]
    [InlineData("сватба", 2)]        // кирилица, малки букви срещу „Сватба“
    [InlineData("АННА", 1)]          // главни букви
    [InlineData("николова", 1)]      // търси и в клиента
    [InlineData("варна", 1)]         // и в мястото
    [InlineData("берлин", 0)]
    public void Search_ByText_IsCaseInsensitive_ForCyrillic(string text, int expected)
    {
        SeedThree();

        Assert.Equal(expected, _db.Search(new EventFilter { Search = text }).Count);
    }

    [Fact]
    public void Search_TreatsLikeWildcardsLiterally()
    {
        SeedThree();

        // Без екраниране "%" би върнал всички записи.
        Assert.Empty(_db.Search(new EventFilter { Search = "%" }));
        Assert.Empty(_db.Search(new EventFilter { Search = "_" }));
    }

    [Fact]
    public void Search_IsSafeAgainstSqlInjection()
    {
        SeedThree();

        var result = _db.Search(new EventFilter { Search = "'; DROP TABLE Events; --" });

        Assert.Empty(result);
        Assert.Equal(3, _db.Count());   // таблицата е непокътната
    }

    [Fact]
    public void Search_CombinesCriteriaWithAnd()
    {
        SeedThree();

        var result = _db.Search(new EventFilter { Type = "wedding", Status = EventStatus.Planned, Search = "анна" });

        Assert.Single(result);
        Assert.Equal("Сватба на Анна и Петър", result[0].Title);
    }
}
