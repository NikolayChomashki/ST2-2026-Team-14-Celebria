namespace Celebria.Models;

/// <summary>Данни за изгледа Events/Index: текущият филтър и намерените събития.</summary>
public class EventIndexViewModel
{
    public EventFilter Filter { get; init; } = new();

    public IReadOnlyList<Event> Events { get; init; } = Array.Empty<Event>();

    /// <summary>Общ брой събития в БД (за текста „Показани X от Y“).</summary>
    public int TotalCount { get; init; }
}
