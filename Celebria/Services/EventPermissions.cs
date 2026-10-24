using System.Globalization;
using System.Security.Claims;
using Celebria.Models;

namespace Celebria.Services;

/// <summary>
/// Кой какво може да прави със събитие. Едно място за правилото – ползва се и от контролера
/// (сървърна проверка), и от изгледите (дали да се покажат бутоните „Редакция“ и „Изтрий“).
/// </summary>
public static class EventPermissions
{
    /// <summary>Администраторът може всичко; обикновеният потребител – само събитията, които сам е създал.</summary>
    public static bool CanModify(this ClaimsPrincipal user, Event ev)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }
        if (user.IsInRole(Roles.Admin))
        {
            return true;
        }
        return ev.OwnerId is { } ownerId && ownerId == user.UserId();
    }

    /// <summary>Статусът на събитие (Планирано / Потвърдено / Проведено / Отказано) се сменя само от администратор.</summary>
    public static bool CanChangeStatus(this ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && user.IsInRole(Roles.Admin);

    /// <summary>Id на влезлия потребител (0, ако няма).</summary>
    public static int UserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;
}
