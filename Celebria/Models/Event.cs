using System.ComponentModel.DataAnnotations;
using Celebria.Validation;

namespace Celebria.Models;

/// <summary>
/// Събитие (сватба, кръщене, рожден ден, фирмено събитие...), което агенцията организира.
/// Записва се в таблица Events на SQLite базата.
/// </summary>
public class Event
{
    public int Id { get; set; }

    [Display(Name = "Наименование")]
    [Required(ErrorMessage = "Наименованието е задължително.")]
    [StringLength(120, MinimumLength = 3, ErrorMessage = "Наименованието трябва да е между 3 и 120 символа.")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Кодът на типа (напр. "wedding"). Допустимите стойности идват от Singleton-а EventTypeCatalog.</summary>
    [Display(Name = "Тип събитие")]
    [Required(ErrorMessage = "Изберете тип събитие.")]
    [KnownEventType]
    public string EventType { get; set; } = string.Empty;

    [Display(Name = "Дата")]
    [Required(ErrorMessage = "Датата е задължителна.")]
    [DataType(DataType.Date)]
    public DateTime? Date { get; set; }

    [Display(Name = "Брой гости")]
    [Required(ErrorMessage = "Брой гости е задължителен.")]
    [Range(1, 5000, ErrorMessage = "Броят гости трябва да е между 1 и 5000.")]
    public int? GuestCount { get; set; }

    [Display(Name = "Клиент")]
    [Required(ErrorMessage = "Името на клиента е задължително.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Името на клиента трябва да е между 2 и 100 символа.")]
    public string ClientName { get; set; } = string.Empty;

    [Display(Name = "Място")]
    [StringLength(150, ErrorMessage = "Мястото може да е най-много 150 символа.")]
    public string? Location { get; set; }

    [Display(Name = "Бюджет (€)")]
    [Range(0, 10_000_000, ErrorMessage = "Бюджетът трябва да е между 0 и 10 000 000 €.")]
    public int? BudgetEur { get; set; }

    [Display(Name = "Статус")]
    public EventStatus Status { get; set; } = EventStatus.Planned;

    [Display(Name = "Бележки")]
    [DataType(DataType.MultilineText)]
    [StringLength(1000, ErrorMessage = "Бележките може да са най-много 1000 символа.")]
    public string? Notes { get; set; }
}
