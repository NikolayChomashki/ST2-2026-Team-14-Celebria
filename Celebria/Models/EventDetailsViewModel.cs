using Celebria.Services;

namespace Celebria.Models;

/// <summary>Данните за страницата „Детайли“: събитието + карта + време.</summary>
public class EventDetailsViewModel
{
    public required Event Event { get; init; }

    /// <summary>Мястото на събитието; null, ако не е въведено.</summary>
    public string? MapQuery { get; init; }

    public string? MapEmbedUrl { get; init; }

    public string? MapLinkUrl { get; init; }

    /// <summary>Null, ако няма място, за което да се търси времето.</summary>
    public WeatherResult? Weather { get; init; }
}
