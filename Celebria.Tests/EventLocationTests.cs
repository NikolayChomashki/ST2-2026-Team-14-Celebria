using Celebria.Models;
using Celebria.Services;

namespace Celebria.Tests;

/// <summary>Тестове за построяването на заявките към Google Maps и OpenWeather от полето „Място“.</summary>
public class EventLocationTests
{
    private static Event WithLocation(string? location) => new() { Location = location };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MapQuery_NoLocation_ReturnsNull(string? location)
    {
        Assert.Null(EventLocation.MapQuery(WithLocation(location)));
        Assert.Empty(EventLocation.WeatherQueries(WithLocation(location), "BG"));
    }

    [Fact]
    public void WeatherQueries_CityAfterLastComma_IsTriedFirst()
    {
        var queries = EventLocation.WeatherQueries(WithLocation(" Хотел „Империал“, Пловдив "), "BG");

        Assert.Equal(new[] { "Пловдив,BG", "Пловдив", "Хотел „Империал“, Пловдив" }, queries);
    }

    [Fact]
    public void WeatherQueries_SingleWord_HasNoDuplicates()
    {
        var queries = EventLocation.WeatherQueries(WithLocation("Банско"), "BG");

        Assert.Equal(new[] { "Банско,BG", "Банско" }, queries);
    }

    [Fact]
    public void WeatherQueries_NoCountryCode_SkipsCityWithCountry()
    {
        var queries = EventLocation.WeatherQueries(WithLocation("Клуб „Петното“, София"), null);

        Assert.Equal(new[] { "София", "Клуб „Петното“, София" }, queries);
    }

    [Fact]
    public void MapEmbedUrl_WithoutKey_UsesKeylessEmbed_AndEscapesQuery()
    {
        var url = EventLocation.MapEmbedUrl("Хотел „Империал“, Пловдив", null);

        Assert.StartsWith("https://maps.google.com/maps?q=", url);
        Assert.EndsWith("&output=embed", url);
        Assert.DoesNotContain(" ", url);
    }

    [Fact]
    public void MapEmbedUrl_WithKey_UsesEmbedApi()
    {
        var url = EventLocation.MapEmbedUrl("Пловдив", "abc123");

        Assert.StartsWith("https://www.google.com/maps/embed/v1/place?key=abc123&q=", url);
    }
}
