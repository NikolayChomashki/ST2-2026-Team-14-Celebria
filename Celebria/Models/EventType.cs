namespace Celebria.Models;

/// <summary>
/// Вид събитие. Неизменяем (record с init свойства), за да може безопасно да се споделя
/// от Singleton-а EventTypeCatalog между всички заявки и нишки.
/// </summary>
/// <param name="Code">Код, който се пази в БД (напр. "wedding").</param>
/// <param name="Name">Име за показване (напр. "Сватба").</param>
/// <param name="Icon">Емоджи за визуализация в списъка.</param>
public sealed record EventType(string Code, string Name, string Icon);
