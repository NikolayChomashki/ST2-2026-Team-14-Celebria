using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using Celebria.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Celebria.Services;

/// <summary>
/// Взема текущото време от OpenWeather: Geocoding API (място → координати), после Current Weather API.
/// Ключът остава на сървъра и никога не се изпраща към браузъра.
/// </summary>
public sealed class OpenWeatherService : IWeatherService
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ExternalApisOptions _options;
    private readonly ILogger<OpenWeatherService> _logger;

    public OpenWeatherService(
        HttpClient http,
        IMemoryCache cache,
        IOptions<ExternalApisOptions> options,
        ILogger<OpenWeatherService> logger)
    {
        _http = http;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WeatherResult> GetWeatherAsync(IEnumerable<string> locationQueries, CancellationToken cancellationToken = default)
    {
        var apiKey = _options.OpenWeatherApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return WeatherResult.Fail("Не е зададен OpenWeather API ключ. Добавете го в appsettings.json (ExternalApis:OpenWeatherApiKey).");
        }

        var queries = locationQueries.Where(q => !string.IsNullOrWhiteSpace(q)).ToList();
        if (queries.Count == 0)
        {
            return WeatherResult.Fail("Няма място, за което да се търси времето.");
        }

        var cacheKey = "weather:" + string.Join("|", queries).ToLowerInvariant();
        if (_cache.TryGetValue(cacheKey, out WeatherResult? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var key = Uri.EscapeDataString(apiKey.Trim());
            foreach (var query in queries)
            {
                var places = await _http.GetFromJsonAsync<GeoDto[]>(
                    $"geo/1.0/direct?q={Uri.EscapeDataString(query)}&limit=1&appid={key}", cancellationToken);
                if (places is not { Length: > 0 })
                {
                    continue;
                }

                var place = places[0];
                var lat = place.Lat.ToString(CultureInfo.InvariantCulture);
                var lon = place.Lon.ToString(CultureInfo.InvariantCulture);
                var weather = await _http.GetFromJsonAsync<WeatherDto>(
                    $"data/2.5/weather?lat={lat}&lon={lon}&units=metric&lang=bg&appid={key}", cancellationToken);
                if (weather?.Main is null)
                {
                    continue;
                }

                var condition = weather.Weather?.FirstOrDefault();
                var result = WeatherResult.Ok(new WeatherInfo(
                    Place: !string.IsNullOrWhiteSpace(weather.Name) ? weather.Name : place.Name ?? query,
                    Country: weather.Sys?.Country ?? place.Country,
                    Temperature: weather.Main.Temp,
                    FeelsLike: weather.Main.FeelsLike,
                    Humidity: weather.Main.Humidity,
                    Pressure: weather.Main.Pressure,
                    WindSpeed: weather.Wind?.Speed ?? 0,
                    Description: condition?.Description ?? "—",
                    IconUrl: condition?.Icon is { Length: > 0 } icon
                        ? $"https://openweathermap.org/img/wn/{icon}@2x.png"
                        : null));

                _cache.Set(cacheKey, result, CacheTime);
                return result;
            }

            return WeatherResult.Fail("OpenWeather не намери такова място. Проверете полето „Място“ – най-добре завършва с града, напр. „Хотел „Империал“, Пловдив“.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("OpenWeather отхвърли API ключа (401).");
            return WeatherResult.Fail("OpenWeather отхвърли API ключа. Проверете го; нов ключ може да се активира до 2 часа.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Грешка при заявка към OpenWeather.");
            return WeatherResult.Fail("Времето не може да бъде получено в момента (проблем с OpenWeather или мрежата).");
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Изтече времето за отговор от OpenWeather.");
            return WeatherResult.Fail("OpenWeather не отговори навреме. Опитайте отново.");
        }
    }

    // --- DTO-та само за нужните полета от JSON отговорите ---

    private sealed class GeoDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("lat")] public double Lat { get; set; }
        [JsonPropertyName("lon")] public double Lon { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
    }

    private sealed class WeatherDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("weather")] public List<ConditionDto>? Weather { get; set; }
        [JsonPropertyName("main")] public MainDto? Main { get; set; }
        [JsonPropertyName("wind")] public WindDto? Wind { get; set; }
        [JsonPropertyName("sys")] public SysDto? Sys { get; set; }
    }

    private sealed class ConditionDto
    {
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("icon")] public string? Icon { get; set; }
    }

    private sealed class MainDto
    {
        [JsonPropertyName("temp")] public double Temp { get; set; }
        [JsonPropertyName("feels_like")] public double FeelsLike { get; set; }
        [JsonPropertyName("humidity")] public int Humidity { get; set; }
        [JsonPropertyName("pressure")] public int Pressure { get; set; }
    }

    private sealed class WindDto
    {
        [JsonPropertyName("speed")] public double Speed { get; set; }
    }

    private sealed class SysDto
    {
        [JsonPropertyName("country")] public string? Country { get; set; }
    }
}
