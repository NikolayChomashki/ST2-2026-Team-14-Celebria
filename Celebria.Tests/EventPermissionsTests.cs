using System.Security.Claims;
using Celebria.Models;
using Celebria.Services;

namespace Celebria.Tests;

/// <summary>Кой може да редактира и изтрива събитие.</summary>
public class EventPermissionsTests
{
    private static ClaimsPrincipal SignedIn(int id, string role) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Claim(ClaimTypes.Role, role)
    }, "Test"));

    private static readonly ClaimsPrincipal Guest = new(new ClaimsIdentity());

    private static Event OwnedBy(int? ownerId) => new() { Id = 1, OwnerId = ownerId };

    [Fact]
    public void User_CanModify_OwnEvent()
    {
        Assert.True(SignedIn(5, Roles.User).CanModify(OwnedBy(5)));
    }

    [Fact]
    public void User_CannotModify_SomeoneElsesEvent()
    {
        Assert.False(SignedIn(5, Roles.User).CanModify(OwnedBy(6)));
    }

    [Fact]
    public void User_CannotModify_EventWithoutOwner()
    {
        // Примерните и по-старите събития нямат собственик – само администратор може да ги пипа.
        Assert.False(SignedIn(5, Roles.User).CanModify(OwnedBy(null)));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(null)]
    public void Admin_CanModify_AnyEvent(int? ownerId)
    {
        Assert.True(SignedIn(1, Roles.Admin).CanModify(OwnedBy(ownerId)));
    }

    [Fact]
    public void OnlyAdmin_CanChangeStatus()
    {
        Assert.True(SignedIn(1, Roles.Admin).CanChangeStatus());
        Assert.False(SignedIn(5, Roles.User).CanChangeStatus());
        Assert.False(Guest.CanChangeStatus());
    }

    [Fact]
    public void Guest_CannotModify_AnyEvent()
    {
        Assert.False(Guest.CanModify(OwnedBy(0)));
        Assert.Equal(0, Guest.UserId());
    }
}
