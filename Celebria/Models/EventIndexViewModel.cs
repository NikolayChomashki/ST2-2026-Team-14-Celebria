namespace Celebria.Models;

/// <summary>Данни за изгледа Events/Index: текущият филтър, намерените събития и търсенето с Gemini.</summary>
public class EventIndexViewModel
{
    public EventFilter Filter { get; init; } = new();

    public IReadOnlyList<Event> Events { get; init; } = Array.Empty<Event>();

    /// <summary>Общ брой събития в БД (за текста „Показани X от Y“).</summary>
    public int TotalCount { get; init; }

    // ---------------------------------------------------------------- търсене с Gemini

    /// <summary>Въведеният текст на естествен език.</summary>
    public string? AiQuery { get; init; }

    /// <summary>True, ако списъкът е резултат от търсене с Gemini.</summary>
    public bool IsAiFiltered { get; init; }

    /// <summary>Как Gemini е разбрал заявката (полето explanation от JSONQuery).</summary>
    public string? AiExplanation { get; init; }

    /// <summary>Генерираният JSONQuery – показва се в „Как е разбрана заявката“.</summary>
    public string? JsonQueryFormatted { get; init; }

    /// <summary>Четима форма на изпълнената SQL заявка (истинската е с параметри).</summary>
    public string? GeneratedSql { get; init; }

    /// <summary>Моделът на Gemini, който е отговорил (може да е резервен).</summary>
    public string? AiModel { get; init; }

    /// <summary>Съобщение, ако Gemini или преводът към SQL не успеят.</summary>
    public string? AiError { get; init; }
}
