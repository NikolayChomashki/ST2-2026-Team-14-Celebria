namespace Celebria.Configuration;

/// <summary>
/// Настройки за външните услуги. Свързва се със секцията "ExternalApis" в appsettings.json (Options pattern).
/// </summary>
public sealed class ExternalApisOptions
{
    public const string SectionName = "ExternalApis";

    /// <summary>Google Maps Embed API ключ. Ако е празен, картата се показва без ключ (неофициален embed URL).</summary>
    public string? GoogleMapsApiKey { get; set; }

    /// <summary>OpenWeather API ключ. Задължителен за показване на времето.</summary>
    public string? OpenWeatherApiKey { get; set; }

    /// <summary>ISO код на държава (напр. BG), който се добавя към града при търсене на времето.</summary>
    public string? DefaultCountryCode { get; set; }

    /// <summary>Google Gemini API ключ (Google AI Studio) за търсене на естествен език. Използва се само на сървъра.</summary>
    public string? GeminiApiKey { get; set; }

    /// <summary>Основен модел на Gemini. При HTTP 503 / 429 / 404 се пробват резервните модели от GeminiQueryService.</summary>
    public string GeminiModel { get; set; } = "gemini-3-flash-preview";
}
