using Microsoft.AspNetCore.Mvc;
using Celebria.Data;
using Celebria.Models;
using Celebria.Services;

namespace Celebria.Controllers;

/// <summary>
/// Събития: списък с филтър (SELECT), добавяне (INSERT) и детайли.
/// Зависимостите идват през конструктора от DI контейнера – и двете са Singleton-и,
/// но контролерът не знае това: работи с интерфейса IEventDatabase.
/// </summary>
public class EventsController : Controller
{
    private readonly IEventDatabase _db;
    private readonly EventTypeCatalog _types;

    public EventsController(IEventDatabase db, EventTypeCatalog types)
    {
        _db = db;
        _types = types;
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

    public IActionResult Details(int id) =>
        _db.GetById(id) is { } ev ? View(ev) : NotFound();

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
}
