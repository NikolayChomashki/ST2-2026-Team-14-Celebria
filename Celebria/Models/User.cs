namespace Celebria.Models;

/// <summary>
/// Потребител (организатор), който може да влиза в приложението. Записва се в таблица Users.
/// Паролата никога не се пази в чист вид – само хеш от <see cref="Services.PasswordHasher"/>.
/// </summary>
public class User
{
    public int Id { get; set; }

    /// <summary>Потребителско име, както е въведено (без интервали в началото и края).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Име за показване в менюто (напр. „Николай Чомашки“).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>PBKDF2 хеш на паролата във формат v1.итерации.сол.хеш (Base64).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary><see cref="Roles.Admin"/> или <see cref="Roles.User"/>.</summary>
    public string Role { get; set; } = Roles.User;

    public DateTime CreatedAt { get; set; }

    public bool IsAdmin => Role == Roles.Admin;
}

/// <summary>Ролите в приложението. Имената се ползват и в [Authorize(Roles = Roles.Admin)].</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";

    /// <summary>Непозната или празна стойност става обикновен потребител (никога админ по погрешка).</summary>
    public static string Normalize(string? role) =>
        string.Equals(role?.Trim(), Admin, StringComparison.OrdinalIgnoreCase) ? Admin : User;

    public static string DisplayName(string role) => role == Admin ? "Администратор" : "Потребител";
}
