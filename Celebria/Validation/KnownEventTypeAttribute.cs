using System.ComponentModel.DataAnnotations;
using Celebria.Services;

namespace Celebria.Validation;

/// <summary>
/// Сървърна проверка, че кодът на типа съществува в <see cref="EventTypeCatalog"/>.
/// Пази от подправени заявки (напр. ръчно променен &lt;select&gt; в браузъра).
/// Тук се вижда „глобалната точка за достъп“ на Singleton-а: атрибутите не могат да
/// получават зависимости през конструктора, затова ползваме EventTypeCatalog.Instance.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class KnownEventTypeAttribute : ValidationAttribute
{
    public KnownEventTypeAttribute() : base("Невалиден тип събитие.") { }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // Празната стойност се обработва от [Required].
        if (value is not string code || string.IsNullOrWhiteSpace(code))
        {
            return ValidationResult.Success;
        }

        return EventTypeCatalog.Instance.Exists(code)
            ? ValidationResult.Success
            : new ValidationResult(
                FormatErrorMessage(validationContext.DisplayName),
                validationContext.MemberName is { } member ? new[] { member } : null);
    }
}
