using Celebria.Models;

namespace Celebria.Services;

/// <summary>
/// Помощни методи: от полето „Място“ на събитието строи заявки към Google Maps и OpenWeather.
/// Мястото е свободен текст, напр. „Хотел „Империал“, Пловдив“ – обикновено градът е след последната запетая.
/// </summary>
public static class EventLocation
{
    /// <summary>Текст за търсене в картата (самото място); null, ако не е въведено.</summary>
    public static string? MapQuery(Event ev) =>
        string.IsNullOrWhiteSpace(ev.Location) ? null : ev.Location.Trim();

    /// <summary>
    /// Кандидати за OpenWeather по ред на приоритет (Chain / Fallback). Геокодерът на OpenWeather разбира
    /// най-добре градове, затова първо се пробва „град,държава“, после само градът, накрая цялото място.
    /// </summary>
    public static IReadOnlyList<string> WeatherQueries(Event ev, string? defaultCountryCode)
    {
        var full = MapQuery(ev);
        if (full is null)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        var city = full.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (!string.IsNullOrEmpty(city))
        {
            if (!string.IsNullOrWhiteSpace(defaultCountryCode))
            {
                list.Add($"{city},{defaultCountryCode.Trim()}");
            }
            list.Add(city);
        }
        list.Add(full);
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>URL за iframe. С ключ – официалният Maps Embed API; без ключ – неофициален embed адрес.</summary>
    public static string MapEmbedUrl(string query, string? apiKey)
    {
        var q = Uri.EscapeDataString(query);
        return string.IsNullOrWhiteSpace(apiKey)
            ? $"https://maps.google.com/maps?q={q}&output=embed"
            : $"https://www.google.com/maps/embed/v1/place?key={Uri.EscapeDataString(apiKey.Trim())}&q={q}";
    }

    /// <summary>Връзка, която отваря мястото в Google Maps в нов таб.</summary>
    public static string MapLinkUrl(string query) =>
        $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(query)}";
}
