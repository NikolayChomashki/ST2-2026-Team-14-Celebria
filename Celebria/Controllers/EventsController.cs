using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Celebria.Configuration;
using Celebria.Data;
using Celebria.Models;
using Celebria.Services;

namespace Celebria.Controllers;

/// <summary>
/// Събития: списък с филтър (SELECT), добавяне (INSERT), редакция (UPDATE), изтриване (DELETE)
/// и детайли с карта (Google Maps) и време (OpenWeather).
/// Зависимостите идват през конструктора от DI контейнера; контролерът работи само с интерфейси.
/// </summary>
public class EventsController : Controller
{
    private readonly IEventDatabase _db;
    private readonly EventTypeCatalog _types;
    private readonly IWeatherService _weather;
    private readonly ExternalApisOptions _apis;

    public EventsController(IEventDatabase db, EventTypeCatalog types, IWeatherService weather, IOptions<ExternalApisOptions> apis)
    {
        _db = db;
        _types = types;
        _weather = weather;
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

        if (!ModelState.IsValid)
        {
            return View(ev);
        }

        var id = _db.Add(ev);
        TempData["Message"] = $"Събитието „{ev.Title}“ е добавено.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public IActionResult Edit(int id) =>
        _db.GetById(id) is { } ev ? View(ev) : NotFound();

    /// <summary>
    /// UPDATE – същата валидация като при Create, но без проверката „дата не в миналото“:
    /// вече проведено събитие трябва да може да се редактира (напр. статус „Проведено“).
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

        if (!ModelState.IsValid)
        {
            return View(ev);
        }

        // Събитието може да е изтрито междувременно (напр. в друг таб).
        if (!_db.Update(ev))
        {
            return NotFound();
        }

        TempData["Message"] = $"Събитието „{ev.Title}“ е обновено.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Страница за потвърждение – GET не изтрива нищо.</summary>
    [HttpGet]
    public IActionResult Delete(int id) =>
        _db.GetById(id) is { } ev ? View(ev) : NotFound();

    /// <summary>DELETE – само чрез POST с anti-forgery token, за да не може да се изтрие с обикновен линк.</summary>
    [HttpPost, ActionName(nameof(Delete)), ValidateAntiForgeryToken]
    public IActionResult DeleteConfirmed(int id)
    {
        var ev = _db.GetById(id);
        if (ev is null || !_db.Delete(id))
        {
            return NotFound();
        }

        TempData["Message"] = $"Събитието „{ev.Title}“ е изтрито.";
        return RedirectToAction(nameof(Index));
    }
}
