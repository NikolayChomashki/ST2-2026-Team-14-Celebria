using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Celebria.Data;
using Celebria.Models;
using Celebria.Services;

namespace Celebria.Controllers;

/// <summary>
/// Вход, регистрация и изход. Ползва вградената cookie автентикация на ASP.NET Core:
/// след успешен вход браузърът получава криптирана бисквитка, а [Authorize] пуска само заявки с нея.
/// </summary>
[AllowAnonymous]
public class AccountController : Controller
{
    private const int SqliteConstraint = 19;
    private const string InvalidLogin = "Грешно потребителско име или парола.";

    private readonly IUserDatabase _users;

    public AccountController(IUserDatabase users) => _users = users;

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(EventsController.Index), "Events");
        }
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Едно и също съобщение за „няма такъв потребител“ и „грешна парола“ –
        // така формата не издава кои потребителски имена съществуват.
        var user = _users.GetUserByUsername(model.Username);
        if (user is null || !PasswordHasher.Verify(model.Password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, InvalidLogin);
            return View(model);
        }

        await SignInAsync(user, model.RememberMe);
        TempData["Message"] = $"Добре дошли, {user.DisplayName}!";

        // Само локални адреси – иначе някой може да подаде линк, който след входа води към чужд сайт.
        return Url.IsLocalUrl(model.ReturnUrl)
            ? LocalRedirect(model.ReturnUrl!)
            : RedirectToAction(nameof(EventsController.Index), "Events");
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(EventsController.Index), "Events");
        }
        return View(new RegisterViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (ModelState.IsValid && _users.UsernameExists(model.Username))
        {
            ModelState.AddModelError(nameof(model.Username), "Това потребителско име вече е заето.");
        }
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = new User
        {
            Username = model.Username,
            DisplayName = model.DisplayName,
            PasswordHash = PasswordHasher.Hash(model.Password)
        };

        try
        {
            _users.AddUser(user);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraint)
        {
            // Две едновременни регистрации с едно име – UNIQUE ограничението в БД отказва втората.
            ModelState.AddModelError(nameof(model.Username), "Това потребителско име вече е заето.");
            return View(model);
        }

        await SignInAsync(user, rememberMe: false);
        TempData["Message"] = $"Регистрацията е успешна. Добре дошли, {user.DisplayName}!";
        return RedirectToAction(nameof(EventsController.Index), "Events");
    }

    /// <summary>Изход – само с POST, за да не може чужда страница да ви изхвърли с обикновен линк.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Message"] = "Излязохте от профила си.";
        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    /// <summary>Страница при опит за достъп до админ панела без роля „Администратор“.</summary>
    [HttpGet]
    public IActionResult AccessDenied() => View();

    private Task SignInAsync(User user, bool rememberMe) =>
        HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AccountClaims.CreatePrincipal(user),
            new AuthenticationProperties { IsPersistent = rememberMe });
}
