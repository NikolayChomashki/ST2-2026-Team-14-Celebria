using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Celebria.Models;

/// <summary>Етап, в който се намира събитието. В БД се пази като текст (името на стойността).</summary>
public enum EventStatus
{
    [Display(Name = "Планирано")] Planned,
    [Display(Name = "Потвърдено")] Confirmed,
    [Display(Name = "Проведено")] Completed,
    [Display(Name = "Отказано")] Cancelled
}

public static class EventStatusExtensions
{
    /// <summary>Българското име от атрибута [Display] (напр. "Потвърдено").</summary>
    public static string DisplayName(this EventStatus status) =>
        typeof(EventStatus).GetField(status.ToString())?
            .GetCustomAttribute<DisplayAttribute>()?.GetName() ?? status.ToString();

    /// <summary>Bootstrap клас за цвета на етикета в таблицата.</summary>
    public static string BadgeClass(this EventStatus status) => status switch
    {
        EventStatus.Confirmed => "text-bg-success",
        EventStatus.Completed => "text-bg-primary",
        EventStatus.Cancelled => "text-bg-danger",
        _ => "text-bg-secondary"
    };
}
