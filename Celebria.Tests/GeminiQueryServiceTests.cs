using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Celebria.Configuration;
using Celebria.Services;

namespace Celebria.Tests;

/// <summary>GeminiQueryService срещу фалшив HTTP сървър – без интернет и без истински ключ.</summary>
public class GeminiQueryServiceTests
{
    private const string TestKey = "test-key-123";

    private const string ValidQueryJson = """
        {"logicalOperator":"AND","conditions":[{"field":"EventType","operator":"equals","value":"wedding"},{"field":"Location","operator":"contains","value":"Пловдив"}],"orderBy":"Date","sortDirection":"ASC","limit":null,"explanation":"Сватби в Пловдив."}
        """;

    private static (GeminiQueryService Service, StubHttpMessageHandler Handler, FixedTimeProvider Time) Create(
        Func<HttpRequestMessage, int, HttpResponseMessage> respond, string? key = TestKey, string model = "gemini-3-flash-preview")
    {
        var handler = new StubHttpMessageHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") };
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero));
        var service = new GeminiQueryService(
            http,
            Options.Create(new ExternalApisOptions { GeminiApiKey = key, GeminiModel = model }),
            new MemoryCache(new MemoryCacheOptions()),
            EventTypeCatalog.Instance,
            time,
            NullLogger<GeminiQueryService>.Instance);
        return (service, handler, time);
    }

    private static HttpResponseMessage Status(HttpStatusCode code) => StubHttpMessageHandler.Json("{\"error\":{}}", code);

    // ------------------------------------------------------------ успешен отговор

    [Fact]
    public async Task ValidResponse_IsParsedIntoJsonQuery()
    {
        var (service, _, _) = Create((_, _) => StubHttpMessageHandler.GeminiText(ValidQueryJson));

        var result = await service.GenerateJsonQueryAsync("сватбите в Пловдив");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Query!.Conditions!.Count);
        Assert.Equal("wedding", result.Query.Conditions[0].Value);
        Assert.Equal("Сватби в Пловдив.", result.Query.Explanation);
        Assert.Equal("gemini-3-flash-preview", result.Model);
        Assert.Contains("\"Пловдив\"", result.RawJson);   // без \uXXXX – четимо в интерфейса
    }

    [Fact]
    public async Task ApiKey_IsSentInHeader_NotInUrl()
    {
        var (service, handler, _) = Create((_, _) => StubHttpMessageHandler.GeminiText(ValidQueryJson));

        await service.GenerateJsonQueryAsync("сватби");

        var request = handler.Requests.Single();
        Assert.Equal(TestKey, request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain(TestKey, request.RequestUri!.ToString());
        Assert.Equal("/v1beta/models/gemini-3-flash-preview:generateContent", request.RequestUri.AbsolutePath);
        Assert.Equal(HttpMethod.Post, request.Method);
    }

    [Fact]
    public async Task RequestBody_HasSystemPromptUserTextLowTemperatureAndJsonMimeType()
    {
        var (service, handler, _) = Create((_, _) => StubHttpMessageHandler.GeminiText(ValidQueryJson));

        await service.GenerateJsonQueryAsync("  сватбите в Пловдив  ");

        using var body = JsonDocument.Parse(handler.Bodies.Single());
        var root = body.RootElement;
        Assert.Contains("JSONQuery", root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        var userPart = root.GetProperty("contents")[0];
        Assert.Equal("user", userPart.GetProperty("role").GetString());
        Assert.Equal("сватбите в Пловдив", userPart.GetProperty("parts")[0].GetProperty("text").GetString());
        var config = root.GetProperty("generationConfig");
        Assert.Equal(0.1, config.GetProperty("temperature").GetDouble());
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
    }

    [Fact]
    public async Task NumericValueWithoutQuotes_IsAccepted()
    {
        var (service, _, _) = Create((_, _) => StubHttpMessageHandler.GeminiText(
            """{"conditions":[{"field":"GuestCount","operator":"gt","value":100}]}"""));

        var result = await service.GenerateJsonQueryAsync("над 100 гости");

        Assert.True(result.IsSuccess);
        Assert.Equal("100", result.Query!.Conditions![0].Value);
    }

    [Fact]
    public async Task MarkdownFencedJson_IsCleaned()
    {
        var (service, _, _) = Create((_, _) => StubHttpMessageHandler.GeminiText("```json\n" + ValidQueryJson + "\n```"));

        Assert.True((await service.GenerateJsonQueryAsync("сватби")).IsSuccess);
    }

    [Theory]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("  {\"a\":1}  ", "{\"a\":1}")]
    [InlineData("```\n{}\n```", "{}")]
    public void ExtractJsonObject_RemovesFences(string raw, string expected)
    {
        Assert.Equal(expected, GeminiQueryService.ExtractJsonObject(raw));
    }

    // ------------------------------------------------------------ проверки преди заявка (без HTTP)

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingApiKey_FailsWithoutCallingGemini(string? key)
    {
        var (service, handler, _) = Create((_, _) => throw new InvalidOperationException("не трябва да се вика"), key);

        var result = await service.GenerateJsonQueryAsync("сватби");

        Assert.False(result.IsSuccess);
        Assert.Contains("GeminiApiKey", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyQuery_FailsWithoutCallingGemini(string query)
    {
        var (service, handler, _) = Create((_, _) => throw new InvalidOperationException());

        Assert.False((await service.GenerateJsonQueryAsync(query)).IsSuccess);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TooLongQuery_FailsWithoutCallingGemini()
    {
        var (service, handler, _) = Create((_, _) => throw new InvalidOperationException());

        var result = await service.GenerateJsonQueryAsync(new string('а', GeminiQueryService.MaxQueryLength + 1));

        Assert.Contains("твърде дълга", result.Error);
        Assert.Empty(handler.Requests);
    }

    // ------------------------------------------------------------ верига от резервни модели

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task RetryableError_FallsBackToNextModel(HttpStatusCode firstStatus)
    {
        var (service, handler, _) = Create((_, n) => n == 1 ? Status(firstStatus) : StubHttpMessageHandler.GeminiText(ValidQueryJson));

        var result = await service.GenerateJsonQueryAsync("сватби");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("gemini-3.1-flash-lite", handler.Requests[1].RequestUri!.ToString());
        Assert.Equal("gemini-3.1-flash-lite", result.Model);
    }

    [Fact]
    public async Task AllModelsOverloaded_TriesEveryModelOnce_ThenFails()
    {
        var (service, handler, _) = Create((_, _) => Status(HttpStatusCode.ServiceUnavailable));

        var result = await service.GenerateJsonQueryAsync("сватби");

        Assert.False(result.IsSuccess);
        Assert.Contains("503", result.Error);
        Assert.Equal(service.CandidateModels().Count, handler.Requests.Count);
    }

    [Fact]
    public async Task AllModelsRateLimited_Reports429()
    {
        var (service, _, _) = Create((_, _) => Status(HttpStatusCode.TooManyRequests));

        Assert.Contains("429", (await service.GenerateJsonQueryAsync("сватби")).Error);
    }

    [Fact]
    public async Task AllModelsMissing_Reports404AndPointsToSettings()
    {
        var (service, _, _) = Create((_, _) => Status(HttpStatusCode.NotFound));

        Assert.Contains("GeminiModel", (await service.GenerateJsonQueryAsync("сватби")).Error);
    }

    [Fact]
    public void CandidateModels_StartWithConfiguredModel_WithoutDuplicates()
    {
        var (service, _, _) = Create((_, _) => throw new InvalidOperationException(), model: "gemini-3.5-flash");

        var models = service.CandidateModels();

        Assert.Equal("gemini-3.5-flash", models[0]);
        Assert.Equal(models.Count, models.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(GeminiQueryService.FallbackModels.Count, models.Count);
    }

    // ------------------------------------------------------------ грешки, които не се повтарят

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task KeyOrRequestRejected_DoesNotRetry(HttpStatusCode status)
    {
        var (service, handler, _) = Create((_, _) => Status(status));

        var result = await service.GenerateJsonQueryAsync("сватби");

        Assert.Contains("ключа", result.Error);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ServerError_ReturnsGenericMessage()
    {
        var (service, handler, _) = Create((_, _) => Status(HttpStatusCode.InternalServerError));

        var result = await service.GenerateJsonQueryAsync("сватби");

        Assert.Contains("HTTP 500", result.Error);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task InvalidJsonFromModel_Fails()
    {
        var (service, _, _) = Create((_, _) => StubHttpMessageHandler.GeminiText("това не е JSON"));

        Assert.Contains("невалиден JSON", (await service.GenerateJsonQueryAsync("сватби")).Error);
    }

    [Fact]
    public async Task BlockedPrompt_Fails()
    {
        var (service, _, _) = Create((_, _) => StubHttpMessageHandler.Json("""{"promptFeedback":{"blockReason":"SAFETY"}}"""));

        Assert.Contains("отказа", (await service.GenerateJsonQueryAsync("нещо")).Error);
    }

    [Fact]
    public async Task EmptyCandidates_Fails()
    {
        var (service, _, _) = Create((_, _) => StubHttpMessageHandler.Json("""{"candidates":[]}"""));

        Assert.Contains("празен", (await service.GenerateJsonQueryAsync("сватби")).Error);
    }

    [Fact]
    public async Task NetworkError_ReturnsFriendlyMessage()
    {
        var (service, _, _) = Create((_, _) => throw new HttpRequestException("DNS"));

        Assert.Contains("Няма връзка", (await service.GenerateJsonQueryAsync("сватби")).Error);
    }

    [Fact]
    public async Task Timeout_ReturnsFriendlyMessage()
    {
        var (service, _, _) = Create((_, _) => throw new TaskCanceledException("timeout"));

        Assert.Contains("не отговори навреме", (await service.GenerateJsonQueryAsync("сватби")).Error);
    }

    [Fact]
    public async Task CancelledByUser_IsNotSwallowed()
    {
        var (service, _, _) = Create((_, _) => throw new TaskCanceledException());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GenerateJsonQueryAsync("сватби", cts.Token));
    }

    // ------------------------------------------------------------ кеш

    [Fact]
    public async Task SameQuerySameDay_IsServedFromCache()
    {
        var (service, handler, _) = Create((_, _) => StubHttpMessageHandler.GeminiText(ValidQueryJson));

        await service.GenerateJsonQueryAsync("Сватбите в Пловдив");
        var second = await service.GenerateJsonQueryAsync("сватбите в пловдив");

        Assert.True(second.IsSuccess);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NextDay_IsNotServedFromCache_BecauseDatesInPromptChange()
    {
        var (service, handler, time) = Create((_, _) => StubHttpMessageHandler.GeminiText(ValidQueryJson));

        await service.GenerateJsonQueryAsync("сватби");
        time.Advance(TimeSpan.FromDays(1));
        await service.GenerateJsonQueryAsync("сватби");

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Failures_AreNotCached()
    {
        var (service, handler, _) = Create((_, n) => n == 1 ? StubHttpMessageHandler.GeminiText("не е JSON") : StubHttpMessageHandler.GeminiText(ValidQueryJson));

        await service.GenerateJsonQueryAsync("сватби");
        var second = await service.GenerateJsonQueryAsync("сватби");

        Assert.True(second.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }
}
