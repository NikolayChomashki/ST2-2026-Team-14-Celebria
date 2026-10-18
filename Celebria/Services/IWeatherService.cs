namespace Celebria.Services;

/// <summary>Договор за услуга, която връща текущото време за място.</summary>
public interface IWeatherService
{
    /// <summary>
    /// Опитва кандидатите за място по ред и връща времето за първото намерено.
    /// Никога не хвърля за очаквани проблеми (липсващ ключ, мрежа) – връща <see cref="WeatherResult.Error"/>.
    /// </summary>
    Task<WeatherResult> GetWeatherAsync(IEnumerable<string> locationQueries, CancellationToken cancellationToken = default);
}
