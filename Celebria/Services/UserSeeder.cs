using Celebria.Data;
using Celebria.Models;

namespace Celebria.Services;

/// <summary>Един профил от секцията "SeedUsers" в настройките.</summary>
public sealed class SeedUserOptions
{
    public const string SectionName = "SeedUsers";

    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = Roles.User;
}

/// <summary>
/// Създава началните профили (напр. администратор и тестов потребител) при старт на приложението.
/// Профил се създава само ако още няма потребител с това име – така паролата, сменена или
/// зададена по-късно, не се презаписва при всяко стартиране.
/// </summary>
public static class UserSeeder
{
    /// <summary>Връща колко нови профила са създадени.</summary>
    public static int Seed(IUserDatabase users, IEnumerable<SeedUserOptions>? seeds)
    {
        var created = 0;
        foreach (var seed in seeds ?? Enumerable.Empty<SeedUserOptions>())
        {
            if (string.IsNullOrWhiteSpace(seed.Username) || string.IsNullOrWhiteSpace(seed.Password)
                || users.UsernameExists(seed.Username))
            {
                continue;
            }

            users.AddUser(new User
            {
                Username = seed.Username,
                DisplayName = string.IsNullOrWhiteSpace(seed.DisplayName) ? seed.Username : seed.DisplayName,
                PasswordHash = PasswordHasher.Hash(seed.Password),
                Role = Roles.Normalize(seed.Role)
            });
            created++;
        }
        return created;
    }
}
