using System.Net;
using System.Text;
using Celebria.Models;
using Celebria.Services;

namespace Celebria.Tests;

/// <summary>Фалшив HTTP сървър: тестовете на GeminiQueryService вървят без интернет и без истински ключ.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _respond;

    public StubHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) => _respond = respond;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> Bodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        return _respond(request, Requests.Count);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Отговор на Gemini, в който моделът е върнал дадения текст.</summary>
    public static HttpResponseMessage GeminiText(string modelText)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize(modelText);
        return Json($$"""{"candidates":[{"content":{"role":"model","parts":[{"text":{{escaped}}}]},"finishReason":"STOP"}]}""");
    }
}

/// <summary>Фиксирана дата – промптът и кешът зависят от днешния ден.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

/// <summary>Фалшива ИИ услуга за тестовете на контролера – връща предварително зададен резултат.</summary>
internal sealed class FakeGeminiQueryService : IGeminiQueryService
{
    private readonly Func<string, GeminiQueryResult> _respond;

    public FakeGeminiQueryService(Func<string, GeminiQueryResult> respond) => _respond = respond;

    public List<string> ReceivedQueries { get; } = new();

    public Task<GeminiQueryResult> GenerateJsonQueryAsync(string naturalLanguageQuery, CancellationToken cancellationToken = default)
    {
        ReceivedQueries.Add(naturalLanguageQuery);
        return Task.FromResult(_respond(naturalLanguageQuery));
    }
}

/// <summary>Услуга за времето, която никога не ходи в интернет.</summary>
internal sealed class NoWeatherService : IWeatherService
{
    public Task<WeatherResult> GetWeatherAsync(IEnumerable<string> locationQueries, CancellationToken cancellationToken = default) =>
        Task.FromResult(WeatherResult.Fail("няма"));
}

internal static class Q
{
    public static QueryCondition C(string field, string op, string value) => new() { Field = field, Operator = op, Value = value };

    public static JSONQuery Of(params QueryCondition[] conditions) => new() { Conditions = conditions.ToList() };
}
