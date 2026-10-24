using Celebria.Models;

namespace Celebria.Services;

/// <summary>
/// Result Object: или валиден <see cref="JSONQuery"/>, или съобщение за грешка за потребителя.
/// Очакваните проблеми (липсващ ключ, мрежа, претоварен модел, невалиден JSON) не са изключения.
/// </summary>
public sealed record GeminiQueryResult(JSONQuery? Query, string? RawJson, string? Model, string? Error)
{
    public bool IsSuccess => Query is not null && Error is null;

    public static GeminiQueryResult Ok(JSONQuery query, string rawJson, string model) => new(query, rawJson, model, null);

    public static GeminiQueryResult Fail(string error) => new(null, null, null, error);
}

/// <summary>
/// Adapter / Facade към ИИ модел: превръща текст на естествен език в <see cref="JSONQuery"/>.
/// Контролерът зависи само от този интерфейс – доставчикът (Gemini, локален модел, фалшива услуга в тестовете)
/// може да се смени, без да се пипа контролерът (Strategy + Dependency Inversion).
/// </summary>
public interface IGeminiQueryService
{
    Task<GeminiQueryResult> GenerateJsonQueryAsync(string naturalLanguageQuery, CancellationToken cancellationToken = default);
}
