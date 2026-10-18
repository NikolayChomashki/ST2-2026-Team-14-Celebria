namespace Celebria.Services;

/// <summary>Текущо време за дадено място (метрични единици).</summary>
public sealed record WeatherInfo(
    string Place,
    string? Country,
    double Temperature,
    double FeelsLike,
    int Humidity,
    int Pressure,
    double WindSpeed,
    string Description,
    string? IconUrl);

/// <summary>Резултат от заявка за време: или данни, или съобщение за грешка за потребителя.</summary>
public sealed record WeatherResult(WeatherInfo? Info, string? Error)
{
    public static WeatherResult Ok(WeatherInfo info) => new(info, null);

    public static WeatherResult Fail(string error) => new(null, error);
}
