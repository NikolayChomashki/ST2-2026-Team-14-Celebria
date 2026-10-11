using System.ComponentModel.DataAnnotations;
using Celebria.Models;

namespace Celebria.Tests;

/// <summary>Unit тестове за валидациите в модела Event (същите, които ASP.NET прави при POST).</summary>
public class EventValidationTests
{
    private static Event Valid() => new()
    {
        Title = "Рожден ден на Никол",
        EventType = "birthday",
        Date = DateTime.Today.AddDays(10),
        GuestCount = 40,
        ClientName = "Петър Христов"
    };

    private static List<ValidationResult> Validate(Event ev)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(ev, new ValidationContext(ev), results, validateAllProperties: true);
        return results;
    }

    private static bool HasErrorFor(List<ValidationResult> results, string member) =>
        results.Any(r => r.MemberNames.Contains(member));

    [Fact]
    public void ValidEvent_HasNoErrors()
    {
        Assert.Empty(Validate(Valid()));
    }

    [Fact]
    public void UnknownEventType_IsRejected_ByCatalog()
    {
        var ev = Valid();
        ev.EventType = "funeral";

        Assert.True(HasErrorFor(Validate(ev), nameof(Event.EventType)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public void Title_TooShortOrMissing_IsRejected(string title)
    {
        var ev = Valid();
        ev.Title = title;

        Assert.True(HasErrorFor(Validate(ev), nameof(Event.Title)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public void GuestCount_OutOfRange_IsRejected(int guests)
    {
        var ev = Valid();
        ev.GuestCount = guests;

        Assert.True(HasErrorFor(Validate(ev), nameof(Event.GuestCount)));
    }

    [Fact]
    public void MissingDate_IsRejected()
    {
        var ev = Valid();
        ev.Date = null;

        Assert.True(HasErrorFor(Validate(ev), nameof(Event.Date)));
    }

    [Fact]
    public void NegativeBudget_IsRejected()
    {
        var ev = Valid();
        ev.BudgetEur = -1;

        Assert.True(HasErrorFor(Validate(ev), nameof(Event.BudgetEur)));
    }
}
