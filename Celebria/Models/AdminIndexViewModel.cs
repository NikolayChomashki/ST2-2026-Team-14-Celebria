namespace Celebria.Models;

/// <summary>Данни за админ панела.</summary>
public class AdminIndexViewModel
{
    public IReadOnlyList<User> Users { get; init; } = Array.Empty<User>();

    /// <summary>Id на влезлия администратор – за него бутоните „Изтрий“ и „Направи потребител“ са скрити.</summary>
    public int CurrentUserId { get; init; }

    public int EventCount { get; init; }

    public int AdminCount => Users.Count(u => u.IsAdmin);
}
