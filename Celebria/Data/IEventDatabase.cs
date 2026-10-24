using Celebria.Models;

namespace Celebria.Data;

/// <summary>
/// Договор за достъп до данните за събития. Контролерите зависят само от този интерфейс,
/// а не от конкретния клас <see cref="Database"/> (Dependency Inversion – буквата D от SOLID).
/// Така в седмица 4 реализацията може да се смени с Repository + EF Core, без да се пипат контролерите.
/// </summary>
public interface IEventDatabase
{
    /// <summary>INSERT – добавя събитие и връща новото Id.</summary>
    int Add(Event ev);

    Event? GetById(int id);

    /// <summary>SELECT с филтър – връща събитията, отговарящи на всички зададени критерии, подредени по дата.</summary>
    IReadOnlyList<Event> Search(EventFilter filter);

    /// <summary>Общ брой събития в базата.</summary>
    int Count();

    /// <summary>SELECT от търсенето с Gemini – изпълнява само заявка, преведена от JsonQuerySqlTranslator.</summary>
    IReadOnlyList<Event> ExecuteSqlSelect(Services.SqlSelectCommand command);

    /// <summary>UPDATE – записва промените по съществуващо събитие. Връща false, ако няма събитие с това Id.</summary>
    bool Update(Event ev);

    /// <summary>DELETE – изтрива събитието. Връща false, ако няма събитие с това Id.</summary>
    bool Delete(int id);
}
