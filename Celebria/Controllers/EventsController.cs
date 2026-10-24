using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Celebria.Configuration;
using Celebria.Data;
using Celebria.Models;
using Celebria.Services;

namespace Celebria.Controllers;

/// <summary>
/// Събития: списък с филтър (SELECT), добавяне (INSERT), редакция (UPDATE), изтриване (DELETE),
/// търсене на естествен език с Gemini (AiSearch) и детайли с карта и време. Достъпно е само след вход ([Authorize]).
/// Зависимостите идват през конструктора от DI контейнера; контролерът работи само с интерфейси.
/// </summary>
[Authorize]
public class EventsController : Controller
{
    private readonly IEventDatabase _db;
    private readonly EventTypeCatalog _types;
    private readonly IWeatherService _weather;
    private readonly IGeminiQueryService _gemini;
    private readonly ExternalApisOptions _apis;

    public EventsController(IEventDatabase db, EventTypeCatalog types, IWeatherService weather,
        IGeminiQueryService gemini, IOptions<ExternalApisOptions> apis)
    {
        _db = db;
        _types = types;
        _weather = weather;
        _gemini = gemini;
        _apis = apis.Value;
    }

    // GET /Events?search=мария&type=wedding&status=Confirmed&from=2026-11-01&to=2026-12-31
    /// <summary>SELECT с филтър – филтрирането се прави в SQL заявката (WHERE), а не в паметта.</summary>
    [HttpGet]
    public IActionResult Index([FromQuery] EventFilter filter)
    {
        // Непознат тип в адреса (ръчно написан URL) – просто се игнорира.
        if (!string.IsNullOrWhiteSpace(filter.Type) && !_types.Exists(filter.Type))
        {
            filter.Type = null;
        }
        // Разменени дати „от“ и „до“ – обръщаме ги вместо да връщаме празен резултат.
        if (filter.From > filter.To)
        {
            (filter.From, filter.To) = (filter.To, filter.From);
        }

        var model = new EventIndexViewModel
        {
            Filter = filter,
            Events = _db.Search(filter),
            TotalCount = _db.Count()
        };
        return View(model);
    }

    // GET /Events/AiSearch?q=потвърдените сватби в Пловдив
    /// <summary>
    /// Търсене на естествен език: текст → Gemini → JSONQuery → JsonQuerySqlTranslator (бял списък)
    /// → параметризиран SELECT. Ако нещо се провали, се показват всички събития и съобщение – никога HTTP 500.
    /// </summary>
    [HttpGet, EnableRateLimiting("ai")]
    public async Task<IActionResult> AiSearch(string? q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return RedirectToAction(nameof(Index));
        }

        var query = q.Trim();
        var result = await _gemini.GenerateJsonQueryAsync(query, cancellationToken);
        if (!result.IsSuccess || result.Query is null)
        {
            return View(nameof(Index), AllEvents(query, result.Error ?? "Търсенето с Gemini не успя."));
        }

        try
        {
            var sql = JsonQuerySqlTranslator.Translate(result.Query, _types);
            return View(nameof(Index), new EventIndexViewModel
            {
                Events = _db.ExecuteSqlSelect(sql),
                TotalCount = _db.Count(),
                AiQuery = query,
                IsAiFiltered = true,
                AiExplanation = result.Query.Explanation,
                JsonQueryFormatted = result.RawJson,
                GeneratedSql = sql.ReadableSql,
                AiModel = result.Model
            });
        }
        catch (ArgumentException ex)
        {
            // Gemini е върнал поле/оператор/стойност извън белия списък – не изпълняваме нищо.
            var model = AllEvents(query, $"Заявката не може да се изпълни: {ex.Message}");
            return View(nameof(Index), new EventIndexViewModel
            {
                Events = model.Events,
                TotalCount = model.TotalCount,
                AiQuery = query,
                AiError = model.AiError,
                JsonQueryFormatted = result.RawJson,
                AiModel = result.Model
            });
        }
    }

    private EventIndexViewModel AllEvents(string aiQuery, string error) => new()
    {
        Events = _db.Search(new EventFilter()),
        TotalCount = _db.Count(),
        AiQuery = aiQuery,
        AiError = error
    };

    /// <summary>Детайли + карта (Google Maps) + текущо време (OpenWeather) за мястото на събитието.</summary>
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        if (_db.GetById(id) is not { } ev)
        {
            return NotFound();
        }

        var mapQuery = EventLocation.MapQuery(ev);
        var model = new EventDetailsViewModel
        {
            Event = ev,
            MapQuery = mapQuery,
            MapEmbedUrl = mapQuery is null ? null : EventLocation.MapEmbedUrl(mapQuery, _apis.GoogleMapsApiKey),
            MapLinkUrl = mapQuery is null ? null : EventLocation.MapLinkUrl(mapQuery),
            Weather = mapQuery is null
                ? null
                : await _weather.GetWeatherAsync(EventLocation.WeatherQueries(ev, _apis.DefaultCountryCode), cancellationToken)
        };
        return View(model);
    }

    [HttpGet]
    public IActionResult Create() => View(new Event
    {
        Date = DateTime.Today.AddDays(30),
        GuestCount = 50,
        Status = EventStatus.Planned
    });

    /// <summary>INSERT – записва новото събитие след сървърна валидация.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Create(
        [Bind(nameof(Event.Title), nameof(Event.EventType), nameof(Event.Date), nameof(Event.GuestCount),
              nameof(Event.ClientName), nameof(Event.Location), nameof(Event.BudgetEur),
              nameof(Event.Status), nameof(Event.Notes))] Event ev)
    {
        if (ev.Date is { } date && date.Date < DateTime.Today)
        {
            ModelState.AddModelError(nameof(Event.Date), "Датата на ново събитие не може да е в миналото.");
        }

        // Обикновеният потребител не избира статус – новото му събитие винаги е „Планирано“,
        // дори някой да изпрати друга стойност с подправена заявка.
        if (!User.CanChangeStatus())
        {
            ev.Status = EventStatus.Planned;
        }

        if (!ModelState.IsValid)
        {
            return View(ev);
        }

        // Собственикът е влезлият потребител – взима се от бисквитката, а не от формата.
        ev.OwnerId = User.UserId();
        var id = _db.Add(ev);
        TempData["Message"] = $"Събитието „{ev.Title}“ е добавено.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public IActionResult Edit(int id)
    {
        if (_db.GetById(id) is not { } ev)
        {
            return NotFound();
        }
        return User.CanModify(ev) ? View(ev) : Forbid();
    }

    /// <summary>
    /// UPDATE – същата валидация като при Create, но без проверката „дата не в миналото“:
    /// вече проведено събитие трябва да може да се редактира (напр. статус „Проведено“).
    /// Правата се проверяват спрямо записа в БД, а не спрямо изпратената форма.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Edit(int id,
        [Bind(nameof(Event.Id), nameof(Event.Title), nameof(Event.EventType), nameof(Event.Date), nameof(Event.GuestCount),
              nameof(Event.ClientName), nameof(Event.Location), nameof(Event.BudgetEur),
              nameof(Event.Status), nameof(Event.Notes))] Event ev)
    {
        if (id != ev.Id)
        {
            return BadRequest();
        }

        // Събитието може да е изтрито междувременно (напр. в друг таб).
        if (_db.GetById(id) is not { } existing)
        {
            return NotFound();
        }
        if (!User.CanModify(existing))
        {
            return Forbid();
        }

        // Статусът се сменя само от администратор – за останалите остава този от базата.
        if (!User.CanChangeStatus())
        {
            ev.Status = existing.Status;
        }

        if (!ModelState.IsValid)
        {
            return View(ev);
        }

        ev.OwnerId = existing.OwnerId;
        if (!_db.Update(ev))
        {
            return NotFound();
        }

        TempData["Message"] = $"Събитието „{ev.Title}“ е обновено.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Страница за потвърждение – GET не изтрива нищо.</summary>
    [HttpGet]
    public IActionResult Delete(int id)
    {
        if (_db.GetById(id) is not { } ev)
        {
            return NotFound();
        }
        return User.CanModify(ev) ? View(ev) : Forbid();
    }

    /// <summary>DELETE – само чрез POST с anti-forgery token, за да не може да се изтрие с обикновен линк.</summary>
    [HttpPost, ActionName(nameof(Delete)), ValidateAntiForgeryToken]
    public IActionResult DeleteConfirmed(int id)
    {
        if (_db.GetById(id) is not { } ev)
        {
            return NotFound();
        }
        if (!User.CanModify(ev))
        {
            return Forbid();
        }
        if (!_db.Delete(id))
        {
            return NotFound();
        }

        TempData["Message"] = $"Събитието „{ev.Title}“ е изтрито.";
        return RedirectToAction(nameof(Index));
    }
}
