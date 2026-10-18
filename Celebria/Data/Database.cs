using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Celebria.Models;

namespace Celebria.Data;

/// <summary>
/// ================================================================================
///  DESIGN PATTERN: SINGLETON (thread-safe, double-checked locking)
/// ================================================================================
///  Проблем:   Всички контролери трябва да работят с една и съща база и една и съща
///             конфигурация (connection string). Схемата на БД трябва да се създаде
///             и напълни с примерни данни точно веднъж при старт на приложението.
///
///  Решение:   Класът <c>Database</c> има само една инстанция за целия процес.
///    1. <c>sealed</c> клас и <c>private</c> конструктор – не може да се създаде с <c>new</c> отвън.
///    2. <c>private static Database? _instance</c> – мястото, където живее единствената инстанция.
///    3. <see cref="Initialize"/> създава инстанцията при първото извикване, а всички
///       следващи извиквания връщат същия обект. <see cref="Instance"/> дава достъп до нея.
///
///  Thread-safety (double-checked locking):
///       if (_instance is null)          // 1-ва проверка – бърза, без lock
///           lock (Lock)                 // само една нишка влиза тук
///               _instance ??= new(...)  // 2-ра проверка – вътре в lock-а
///       Без втората проверка две нишки, минали едновременно първата, биха създали два обекта.
///
///  Връзка с DI: В Program.cs: <c>AddSingleton&lt;IEventDatabase&gt;(_ =&gt; Database.Initialize(cs))</c>.
///       Контролерите получават инстанцията през конструктора и зависят само от интерфейса
///       <see cref="IEventDatabase"/>, което компенсира основния недостатък на Singleton –
///       трудното тестване и скритите зависимости.
///
///  Защо това е безопасно: класът не пази отворена връзка. Всеки метод отваря собствена
///       SqliteConnection (Microsoft.Data.Sqlite има connection pooling), така че една
///       споделена инстанция може да обслужва паралелни заявки.
///
///  Тестване: <see cref="CreateIsolated"/> е internal и се вижда само от Celebria.Tests.
///       Така тестовете работят с отделна временна база, без да пипат глобалната инстанция.
/// ================================================================================
/// </summary>
public sealed class Database : IEventDatabase
{
    private static readonly object Lock = new();
    private static Database? _instance;

    private const string DateFormat = "yyyy-MM-dd";
    private const string Columns =
        "Id, Title, EventType, Date, GuestCount, ClientName, Location, BudgetEur, Status, Notes";

    private readonly string _connectionString;

    private Database(string connectionString, bool seed)
    {
        _connectionString = connectionString;
        EnsureCreated(seed);
    }

    /// <summary>Единственият екземпляр. Преди първо ползване трябва да се извика <see cref="Initialize"/>.</summary>
    public static Database Instance =>
        _instance ?? throw new InvalidOperationException(
            "Database не е инициализирана. Извикайте Database.Initialize(connectionString).");

    /// <summary>Създава екземпляра при първо извикване (thread-safe); следващите извиквания връщат същия.</summary>
    public static Database Initialize(string connectionString)
    {
        if (_instance is null)
        {
            lock (Lock)
            {
                _instance ??= new Database(connectionString, seed: true);
            }
        }
        return _instance;
    }

    /// <summary>
    /// Само за тестове: отделна инстанция върху друга (временна) база, без примерни данни.
    /// Не е част от публичния API, за да не се нарушава идеята на Singleton в приложението.
    /// </summary>
    internal static Database CreateIsolated(string connectionString, bool seed = false) =>
        new(connectionString, seed);

    // ---------------------------------------------------------------- схема и примерни данни

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureCreated(bool seed)
    {
        var dataSource = new SqliteConnectionStringBuilder(_connectionString).DataSource;
        var dir = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var connection = Open();
        using (var cmd = connection.CreateCommand())
        {
            // Date се пази като текст yyyy-MM-dd: така сравненията >= и <= в SQL работят правилно.
            // SearchKey е нормализиран текст (малки букви) за търсене без значение от регистъра –
            // вграденият LIKE/NOCASE в SQLite не прави това за кирилица.
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS Events (
                    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title      TEXT    NOT NULL,
                    EventType  TEXT    NOT NULL,
                    Date       TEXT    NOT NULL,
                    GuestCount INTEGER NOT NULL CHECK (GuestCount > 0),
                    ClientName TEXT    NOT NULL,
                    Location   TEXT    NULL,
                    BudgetEur  INTEGER NULL CHECK (BudgetEur IS NULL OR BudgetEur >= 0),
                    Status     TEXT    NOT NULL,
                    Notes      TEXT    NULL,
                    SearchKey  TEXT    NOT NULL,
                    CreatedAt  TEXT    NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_Events_Date ON Events (Date);
                CREATE INDEX IF NOT EXISTS IX_Events_EventType ON Events (EventType);
                """;
            cmd.ExecuteNonQuery();
        }

        if (seed && CountRows(connection) == 0)
        {
            Seed(connection);
        }
    }

    private static void Seed(SqliteConnection connection)
    {
        var today = DateTime.Today;
        var samples = new List<Event>
        {
            New("Сватба на Мария и Иван",          "wedding",    today.AddDays(36),  120, "Мария Георгиева",   "Хотел „Империал“, Пловдив",     18000, EventStatus.Confirmed),
            New("Кръщене на София",                "baptism",    today.AddDays(44),   45, "Елена Николова",    "Ресторант „Хемингуей“, Пловдив", 3500, EventStatus.Planned),
            New("Рожден ден на Никол – 18 г.",     "birthday",   today.AddDays(57),   60, "Петър Христов",     "Клуб „Петното“, София",          4200, EventStatus.Confirmed),
            New("Сватба на Анна и Петър",          "wedding",    today.AddDays(64),  180, "Анна Стефанова",    "Комплекс „Старосел“",           26000, EventStatus.Planned),
            New("Коледно парти на Musala Soft",    "corporate",  today.AddDays(75),  250, "Musala Soft",       "Интер Експо Център, София",     32000, EventStatus.Confirmed),
            New("Бал на 12 „б“ клас",              "prom",       today.AddDays(230), 140, "Калина Маринова",   "Гранд Хотел Пловдив",           15000, EventStatus.Planned),
            New("Tech Conference Plovdiv 2027",    "conference", today.AddDays(150), 400, "Пловдив Тех ООД",   "Международен панаир Пловдив",   45000, EventStatus.Planned),
            New("Кръщене на Александър",           "baptism",    today.AddDays(20),   35, "Борис Андреев",     "Църква „Св. Марина“, Пловдив",   2500, EventStatus.Confirmed),
            New("Рожден ден на Георги – 40 г.",    "birthday",   today.AddDays(-12),  50, "Георги Димитров",   "Механа „Чучура“, Пловдив",       3000, EventStatus.Completed),
            New("Тиймбилдинг на Chaos",            "corporate",  today.AddDays(-30),  80, "Chaos",             "Банско",                        21000, EventStatus.Completed),
            New("Годишнина от сватба – 25 г.",     "other",      today.AddDays(95),   70, "Десислава Тодорова","Винарна „Дамяница“",             6000, EventStatus.Planned),
            New("Сватба на Лора и Мартин",         "wedding",    today.AddDays(5),   150, "Лора Савова",       "Хотел „Рила“, Боровец",         22000, EventStatus.Cancelled)
        };

        using var tx = connection.BeginTransaction();
        foreach (var ev in samples)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = InsertSql;
            Bind(cmd, ev);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();

        static Event New(string title, string type, DateTime date, int guests, string client,
                         string location, int budget, EventStatus status) => new()
        {
            Title = title, EventType = type, Date = date, GuestCount = guests,
            ClientName = client, Location = location, BudgetEur = budget, Status = status
        };
    }

    // ---------------------------------------------------------------- INSERT

    private const string InsertSql = """
        INSERT INTO Events (Title, EventType, Date, GuestCount, ClientName, Location, BudgetEur, Status, Notes, SearchKey, CreatedAt)
        VALUES ($title, $type, $date, $guests, $client, $location, $budget, $status, $notes, $searchKey, $createdAt);
        """;

    /// <summary>INSERT – стойностите винаги се подават като параметри (защита от SQL injection).</summary>
    public int Add(Event ev)
    {
        ArgumentNullException.ThrowIfNull(ev);

        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = InsertSql + "SELECT last_insert_rowid();";
        Bind(cmd, ev);
        var id = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        ev.Id = id;
        return id;
    }

    private static void Bind(SqliteCommand cmd, Event ev)
    {
        if (ev.Date is null || ev.GuestCount is null)
        {
            throw new ArgumentException("Датата и броят гости са задължителни.", nameof(ev));
        }

        var title = ev.Title.Trim();
        var client = ev.ClientName.Trim();
        var location = string.IsNullOrWhiteSpace(ev.Location) ? null : ev.Location.Trim();

        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$type", ev.EventType.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$date", ev.Date.Value.ToString(DateFormat, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$guests", ev.GuestCount.Value);
        cmd.Parameters.AddWithValue("$client", client);
        cmd.Parameters.AddWithValue("$location", (object?)location ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$budget", (object?)ev.BudgetEur ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", ev.Status.ToString());
        cmd.Parameters.AddWithValue("$notes", string.IsNullOrWhiteSpace(ev.Notes) ? DBNull.Value : ev.Notes.Trim());
        cmd.Parameters.AddWithValue("$searchKey", Normalize($"{title} {client} {location}"));
        cmd.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    // ---------------------------------------------------------------- UPDATE и DELETE

    /// <summary>UPDATE – CreatedAt не се променя; SearchKey се пресмята наново от новите стойности.</summary>
    public bool Update(Event ev)
    {
        ArgumentNullException.ThrowIfNull(ev);

        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Events
            SET Title = $title, EventType = $type, Date = $date, GuestCount = $guests,
                ClientName = $client, Location = $location, BudgetEur = $budget,
                Status = $status, Notes = $notes, SearchKey = $searchKey
            WHERE Id = $id;
            """;
        Bind(cmd, ev);
        cmd.Parameters.AddWithValue("$id", ev.Id);
        return cmd.ExecuteNonQuery() == 1;
    }

    public bool Delete(int id)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Events WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 1;
    }

    // ---------------------------------------------------------------- SELECT

    public Event? GetById(int id)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM Events WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>
    /// SELECT с филтър. WHERE клаузата се сглобява само от зададените критерии;
    /// текстът на SQL-а идва от константи в кода, а всички стойности са параметри.
    /// </summary>
    public IReadOnlyList<Event> Search(EventFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        using var connection = Open();
        using var cmd = connection.CreateCommand();
        var where = new List<string>();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            where.Add(@"SearchKey LIKE $search ESCAPE '\'");
            cmd.Parameters.AddWithValue("$search", $"%{EscapeLike(Normalize(filter.Search))}%");
        }
        if (!string.IsNullOrWhiteSpace(filter.Type))
        {
            where.Add("EventType = $type");
            cmd.Parameters.AddWithValue("$type", filter.Type.Trim().ToLowerInvariant());
        }
        if (filter.Status is { } status)
        {
            where.Add("Status = $status");
            cmd.Parameters.AddWithValue("$status", status.ToString());
        }
        if (filter.From is { } from)
        {
            where.Add("Date >= $from");
            cmd.Parameters.AddWithValue("$from", from.ToString(DateFormat, CultureInfo.InvariantCulture));
        }
        if (filter.To is { } to)
        {
            where.Add("Date <= $to");
            cmd.Parameters.AddWithValue("$to", to.ToString(DateFormat, CultureInfo.InvariantCulture));
        }

        var sql = new StringBuilder($"SELECT {Columns} FROM Events");
        if (where.Count > 0)
        {
            sql.Append(" WHERE ").Append(string.Join(" AND ", where));
        }
        sql.Append(" ORDER BY Date, Title");
        cmd.CommandText = sql.ToString();

        using var reader = cmd.ExecuteReader();
        var result = new List<Event>();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public int Count()
    {
        using var connection = Open();
        return CountRows(connection);
    }

    private static int CountRows(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Events";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- помощни

    private static Event Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Title = r.GetString(1),
        EventType = r.GetString(2),
        Date = DateTime.ParseExact(r.GetString(3), DateFormat, CultureInfo.InvariantCulture),
        GuestCount = r.GetInt32(4),
        ClientName = r.GetString(5),
        Location = r.IsDBNull(6) ? null : r.GetString(6),
        BudgetEur = r.IsDBNull(7) ? null : r.GetInt32(7),
        Status = Enum.TryParse<EventStatus>(r.GetString(8), out var s) ? s : EventStatus.Planned,
        Notes = r.IsDBNull(9) ? null : r.GetString(9)
    };

    /// <summary>Малки букви и единични интервали – работи и за кирилица (за разлика от NOCASE в SQLite).</summary>
    internal static string Normalize(string text) =>
        Regex.Replace(text.Trim(), @"\s+", " ").ToLowerInvariant();

    /// <summary>Екранира %, _ и \, за да се търсят буквално, а не като шаблони на LIKE.</summary>
    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
