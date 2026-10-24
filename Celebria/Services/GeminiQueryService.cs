using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Celebria.Configuration;
using Celebria.Models;

namespace Celebria.Services;

/// <summary>
/// Клиент към Google Gemini REST API (v1beta/models/{model}:generateContent), без външни SDK пакети –
/// само HttpClient от IHttpClientFactory и System.Text.Json.
///  - Chain of Responsibility / Fallback: при HTTP 503, 429 или 404 се пробва следващият модел от веригата;
///  - Result Object: грешките се връщат като <see cref="GeminiQueryResult.Fail"/>, не като изключения;
///  - Cache-Aside: еднаква заявка в един и същи ден се връща от кеша за 10 минути (пести безплатния лимит);
///  - ключът е в хедъра x-goog-api-key, а не в адреса – не попада в логове и история на заявки;
///  - temperature 0.1 и responseMimeType application/json – предвидим, синтактично валиден JSON.
/// </summary>
public sealed class GeminiQueryService : IGeminiQueryService
{
    public const int MaxQueryLength = 300;
    private const string DefaultModel = "gemini-3-flash-preview";
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(10);

    /// <summary>Резервни модели от поколение Gemini 3+ (по ръководството) – пробват се след основния.</summary>
    public static readonly IReadOnlyList<string> FallbackModels = new[]
    {
        "gemini-3-flash-preview",
        "gemini-3.1-flash-lite",
        "gemini-3.5-flash",
        "gemini-3.8-flash"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HttpClient _http;
    private readonly ExternalApisOptions _options;
    private readonly IMemoryCache _cache;
    private readonly EventTypeCatalog _types;
    private readonly TimeProvider _time;
    private readonly ILogger<GeminiQueryService> _logger;

    public GeminiQueryService(
        HttpClient http,
        IOptions<ExternalApisOptions> options,
        IMemoryCache cache,
        EventTypeCatalog types,
        TimeProvider time,
        ILogger<GeminiQueryService> logger)
    {
        _http = http;
        _options = options.Value;
        _cache = cache;
        _types = types;
        _time = time;
        _logger = logger;
    }

    /// <summary>Основният модел от настройките, следван от резервните (без повторения).</summary>
    public IReadOnlyList<string> CandidateModels()
    {
        var primary = string.IsNullOrWhiteSpace(_options.GeminiModel) ? DefaultModel : _options.GeminiModel.Trim();
        return new[] { primary }.Concat(FallbackModels).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<GeminiQueryResult> GenerateJsonQueryAsync(string naturalLanguageQuery, CancellationToken cancellationToken = default)
    {
        var query = naturalLanguageQuery?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            return GeminiQueryResult.Fail("Въведете какво търсите, напр. „потвърдените сватби в Пловдив“.");
        }
        if (query.Length > MaxQueryLength)
        {
            return GeminiQueryResult.Fail($"Заявката е твърде дълга (максимум {MaxQueryLength} символа).");
        }

        var apiKey = _options.GeminiApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            return GeminiQueryResult.Fail("Не е зададен Gemini API ключ. Добавете го в appsettings.json (ExternalApis:GeminiApiKey) или в User Secrets.");
        }

        var today = _time.GetLocalNow().Date;
        var cacheKey = $"gemini:{today:yyyy-MM-dd}:{query.ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out GeminiQueryResult? cached) && cached is not null)
        {
            return cached;
        }

        var payload = JsonSerializer.Serialize(new GeminiRequest
        {
            SystemInstruction = new GeminiContent { Parts = { new GeminiPart { Text = EventQueryPrompt.Build(_types, today) } } },
            Contents = { new GeminiContent { Role = "user", Parts = { new GeminiPart { Text = query } } } },
            GenerationConfig = new GeminiGenerationConfig()
        }, JsonOptions);

        var lastStatus = HttpStatusCode.ServiceUnavailable;
        try
        {
            foreach (var model in CandidateModels())
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-goog-api-key", apiKey);

                using var response = await _http.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    lastStatus = response.StatusCode;
                    _logger.LogWarning("Gemini модел {Model} върна HTTP {Status}.", model, (int)response.StatusCode);

                    // Претоварен модел, лимит или пенсиониран модел – пробваме следващия във веригата.
                    if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests or HttpStatusCode.NotFound)
                    {
                        continue;
                    }

                    return response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                        ? GeminiQueryResult.Fail("Gemini отхвърли заявката или API ключа. Проверете ExternalApis:GeminiApiKey.")
                        : GeminiQueryResult.Fail($"Грешка от Gemini (HTTP {(int)response.StatusCode}). Опитайте отново.");
                }

                var result = ParseResponse(body, model);
                if (result.IsSuccess)
                {
                    _cache.Set(cacheKey, result, CacheTime);
                }
                return result;
            }

            return lastStatus switch
            {
                HttpStatusCode.TooManyRequests => GeminiQueryResult.Fail("Лимитът на заявки към Gemini е временно достигнат (HTTP 429). Опитайте след малко."),
                HttpStatusCode.NotFound => GeminiQueryResult.Fail("Нито един от моделите на Gemini не е наличен (HTTP 404). Проверете ExternalApis:GeminiModel."),
                _ => GeminiQueryResult.Fail("Сървърите на Gemini са временно претоварени (HTTP 503). Опитайте отново след няколко секунди.")
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Мрежова грешка при връзка с Gemini.");
            return GeminiQueryResult.Fail("Няма връзка с Gemini. Проверете интернет връзката.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Gemini не отговори навреме.");
            return GeminiQueryResult.Fail("Gemini не отговори навреме. Опитайте отново.");
        }
    }

    /// <summary>Чете отговора на Gemini: текста на първия кандидат → JSONQuery.</summary>
    private GeminiQueryResult ParseResponse(string body, string model)
    {
        try
        {
            var response = JsonSerializer.Deserialize<GeminiResponse>(body, JsonOptions);
            if (!string.IsNullOrEmpty(response?.PromptFeedback?.BlockReason))
            {
                return GeminiQueryResult.Fail("Gemini отказа да обработи тази заявка. Преформулирайте я.");
            }

            var text = string.Concat(response?.Candidates?.FirstOrDefault()?.Content?.Parts?.Select(p => p.Text) ?? Enumerable.Empty<string?>());
            if (string.IsNullOrWhiteSpace(text))
            {
                return GeminiQueryResult.Fail("Gemini върна празен отговор. Опитайте отново.");
            }

            var jsonQuery = JsonSerializer.Deserialize<JSONQuery>(ExtractJsonObject(text), JsonOptions);
            if (jsonQuery is null)
            {
                return GeminiQueryResult.Fail("Отговорът на Gemini не можа да се разчете като JSONQuery.");
            }

            jsonQuery.Conditions ??= new List<QueryCondition>();
            return GeminiQueryResult.Ok(jsonQuery, JsonSerializer.Serialize(jsonQuery, JsonOptions), model);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Невалиден JSON от Gemini.");
            return GeminiQueryResult.Fail("Gemini върна невалиден JSON. Опитайте отново или преформулирайте заявката.");
        }
    }

    /// <summary>Маха евентуални markdown ограждения (```json ... ```), ако моделът ги е добавил.</summary>
    public static string ExtractJsonObject(string rawText)
    {
        var text = rawText.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = text.IndexOf('\n');
            text = firstNewLine >= 0 ? text[(firstNewLine + 1)..] : text.TrimStart('`');
            if (text.EndsWith("```", StringComparison.Ordinal))
            {
                text = text[..^3];
            }
        }
        return text.Trim();
    }

    // ---------------------------------------------------------------- DTO за generateContent

    private sealed class GeminiRequest
    {
        [JsonPropertyName("systemInstruction")] public GeminiContent? SystemInstruction { get; set; }
        [JsonPropertyName("contents")] public List<GeminiContent> Contents { get; set; } = new();
        [JsonPropertyName("generationConfig")] public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("parts")] public List<GeminiPart> Parts { get; set; } = new();
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")] public double Temperature { get; set; } = 0.1;
        [JsonPropertyName("responseMimeType")] public string ResponseMimeType { get; set; } = "application/json";
        [JsonPropertyName("maxOutputTokens")] public int MaxOutputTokens { get; set; } = 1024;
    }

    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")] public List<GeminiCandidate>? Candidates { get; set; }
        [JsonPropertyName("promptFeedback")] public GeminiPromptFeedback? PromptFeedback { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")] public GeminiContent? Content { get; set; }
    }

    private sealed class GeminiPromptFeedback
    {
        [JsonPropertyName("blockReason")] public string? BlockReason { get; set; }
    }
}
