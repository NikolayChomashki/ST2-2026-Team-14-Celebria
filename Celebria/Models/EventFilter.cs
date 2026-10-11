namespace Celebria.Models;

/// <summary>
/// Критерии за филтриране на списъка със събития (SELECT ... WHERE ...).
/// Всички полета са незадължителни – празно поле означава „без ограничение“.
/// Стойностите идват от query string-а, напр. /Events?type=wedding&amp;status=Confirmed.
/// </summary>
public class EventFilter
{
    /// <summary>Текст, търсен в наименованието, клиента и мястото (без значение от регистъра).</summary>
    public string? Search { get; set; }

    /// <summary>Код на тип от EventTypeCatalog.</summary>
    public string? Type { get; set; }

    public EventStatus? Status { get; set; }

    /// <summary>От дата (включително).</summary>
    public DateTime? From { get; set; }

    /// <summary>До дата (включително).</summary>
    public DateTime? To { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Search) && string.IsNullOrWhiteSpace(Type)
        && Status is null && From is null && To is null;
}
