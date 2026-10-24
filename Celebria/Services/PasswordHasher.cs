using System.Globalization;
using System.Security.Cryptography;

namespace Celebria.Services;

/// <summary>
/// Хеширане на пароли с PBKDF2 (HMAC-SHA256, 100 000 итерации, случайна 16-байтова сол).
/// Ползва само вградения в .NET <see cref="Rfc2898DeriveBytes"/> – без допълнителни пакети.
/// Форматът е „v1.итерации.сол.хеш“, за да може в бъдеще броят итерации да се увеличи,
/// без старите хешове да спрат да работят.
/// </summary>
public static class PasswordHasher
{
    private const string Version = "v1";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return string.Join('.', Version, Iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>Проверява паролата. Сравнението е с постоянно време, за да не издава колко байта съвпадат.</summary>
    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('.');
        if (parts.Length != 4 || parts[0] != Version
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
