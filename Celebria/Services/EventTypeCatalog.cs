using System.Collections.Frozen;
using Celebria.Models;

namespace Celebria.Services;

/// <summary>
/// ================================================================================
///  DESIGN PATTERN: SINGLETON (класическа GoF форма с Lazy&lt;T&gt;)
/// ================================================================================
///  Проблем:   Видовете събития („Сватба“, „Кръщене“...) трябва да са едни и същи
///             навсякъде – във формата, във филтъра, във валидацията и в списъка.
///             Ако всяка част си създава собствен списък, те лесно се разминават.
///
///  Решение:   Един-единствен каталог за цялото приложение.
///    1. Класът е <c>sealed</c> – не може да се наследи и да се „заобиколи“ ограничението.
///    2. Конструкторът е <c>private</c> – никой извън класа не може да извика <c>new</c>.
///    3. Единствената инстанция се пази в <c>private static readonly Lazy&lt;T&gt;</c>
///       и се достъпва през <see cref="Instance"/> (глобална точка за достъп).
///
///  Thread-safety:  <see cref="Lazy{T}"/> с <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>
///             гарантира, че дори при едновременни заявки конструкторът се изпълнява
///             точно веднъж. Това е еквивалентът на „double-checked locking“ от лекцията
///             (виж <see cref="Celebria.Data.Database"/>), но кодът е по-кратък.
///
///  Споделено състояние без рискове: данните са неизменяеми (record + FrozenDictionary +
///             ReadOnlyCollection), затова инстанцията може да се ползва паралелно без lock.
///
///  Употреба:  1) директно – <c>EventTypeCatalog.Instance</c> (напр. в KnownEventTypeAttribute);
///             2) през DI – в Program.cs: <c>builder.Services.AddSingleton(EventTypeCatalog.Instance)</c>,
///                после се инжектира в контролера и във изгледите (@inject).
/// ================================================================================
/// </summary>
public sealed class EventTypeCatalog
{
    private static readonly Lazy<EventTypeCatalog> LazyInstance =
        new(() => new EventTypeCatalog(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Брояч на създадените инстанции – използва се само в тестовете (трябва да е 1).</summary>
    internal static int CreatedInstances;

    private readonly IReadOnlyList<EventType> _all;
    private readonly FrozenDictionary<string, EventType> _byCode;

    private EventTypeCatalog()
    {
        Interlocked.Increment(ref CreatedInstances);

        _all = new List<EventType>
        {
            new("wedding",    "Сватба"),
            new("baptism",    "Кръщене"),
            new("birthday",   "Рожден ден"),
            new("corporate",  "Фирмено събитие"),
            new("prom",       "Бал / Дипломиране"),
            new("conference", "Конференция"),
            new("other",      "Друго")
        }.AsReadOnly();

        _byCode = _all.ToFrozenDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Единствената инстанция на каталога.</summary>
    public static EventTypeCatalog Instance => LazyInstance.Value;

    /// <summary>Всички видове събития в реда, в който се показват.</summary>
    public IReadOnlyList<EventType> All => _all;

    public bool Exists(string? code) => code is not null && _byCode.ContainsKey(code);

    public EventType? Find(string? code) =>
        code is not null && _byCode.TryGetValue(code, out var type) ? type : null;

    /// <summary>Име за показване; ако кодът е непознат – връща самия код.</summary>
    public string NameOf(string? code) => Find(code)?.Name ?? code ?? string.Empty;
}
