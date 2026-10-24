using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Celebria.Data;
using Celebria.Models;

namespace Celebria.Controllers;

/// <summary>
/// Админ панел: списък с потребителите, смяна на роля и изтриване на профил.
/// Достъпен е само за роля „Администратор“ – останалите получават страница „Нямате достъп“.
/// </summary>
[Authorize(Roles = Roles.Admin)]
public class AdminController : Controller
{
    private readonly IUserDatabase _users;
    private readonly IEventDatabase _events;

    public AdminController(IUserDatabase users, IEventDatabase events)
    {
        _users = users;
        _events = events;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var model = new AdminIndexViewModel
        {
            Users = _users.GetAllUsers(),
            CurrentUserId = CurrentUserId(),
            EventCount = _events.Count()
        };
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SetRole(int id, string role)
    {
        var user = _users.GetUserById(id);
        if (user is null)
        {
            return NotFound();
        }

        var newRole = Roles.Normalize(role);
        if (id == CurrentUserId() && newRole != Roles.Admin)
        {
            TempData["Error"] = "Не можете да махнете собствените си администраторски права.";
        }
        else if (user.IsAdmin && newRole != Roles.Admin && _users.CountAdmins() <= 1)
        {
            TempData["Error"] = "Трябва да остане поне един администратор.";
        }
        else
        {
            _users.SetRole(id, newRole);
            TempData["Message"] = $"„{user.DisplayName}“ вече е {Roles.DisplayName(newRole).ToLowerInvariant()}.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult DeleteUser(int id)
    {
        var user = _users.GetUserById(id);
        if (user is null)
        {
            return NotFound();
        }

        if (id == CurrentUserId())
        {
            TempData["Error"] = "Не можете да изтриете собствения си профил.";
        }
        else if (user.IsAdmin && _users.CountAdmins() <= 1)
        {
            TempData["Error"] = "Трябва да остане поне един администратор.";
        }
        else
        {
            _users.DeleteUser(id);
            TempData["Message"] = $"Профилът „{user.DisplayName}“ е изтрит.";
        }
        return RedirectToAction(nameof(Index));
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
}
