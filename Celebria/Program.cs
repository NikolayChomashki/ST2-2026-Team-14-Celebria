using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Celebria.Configuration;
using Celebria.Data;
using Celebria.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Вход с бисквитка (cookie authentication). Ако гост отвори защитена страница,
// се пренасочва към /Account/Login?ReturnUrl=..., а след входа се връща обратно.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "Celebria.Auth";
        options.Cookie.HttpOnly = true;                       // JavaScript не може да прочете бисквитката
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);       // важи при „Запомни ме“
        options.SlidingExpiration = true;

        // При всяка заявка сверяваме бисквитката с базата: изтрит профил излиза веднага,
        // а сменена роля (напр. администратор → потребител) важи от следващата заявка.
        options.Events.OnValidatePrincipal = async context =>
        {
            var users = context.HttpContext.RequestServices.GetRequiredService<IUserDatabase>();
            var idValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = int.TryParse(idValue, out var id) ? users.GetUserById(id) : null;
            // Профилът трябва да съществува и да е същият (Id + потребителско име) –
            // иначе бисквитка от друга/пресъздадена база би могла да „стане“ чужд профил със същото Id.
            if (user is null || !string.Equals(context.Principal!.FindFirstValue(ClaimTypes.Name), user.Username, StringComparison.Ordinal))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }
            if (context.Principal!.FindFirstValue(ClaimTypes.Role) != user.Role)
            {
                context.ReplacePrincipal(AccountClaims.CreatePrincipal(user));
                context.ShouldRenew = true;
            }
        };
    });
builder.Services.AddAuthorization();

// Външни API-та (Google Maps / OpenWeather). Ключовете се попълват в appsettings.json (секция ExternalApis).
builder.Services.Configure<ExternalApisOptions>(builder.Configuration.GetSection(ExternalApisOptions.SectionName));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IWeatherService, OpenWeatherService>(client =>
{
    client.BaseAddress = new Uri("https://api.openweathermap.org/");
    client.Timeout = TimeSpan.FromSeconds(8);
});

// Търсене на естествен език с Google Gemini (Adapter/Facade зад IGeminiQueryService).
// HttpClient идва от IHttpClientFactory – без ръчно `new HttpClient()` и изчерпване на сокети.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IGeminiQueryService, GeminiQueryService>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
});

// Ограничение: до 10 търсения с Gemini в минута на потребител – пази безплатния лимит и от злоупотреба.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("ai", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync("Твърде много търсения с Gemini. Изчакайте минута и опитайте отново.", token);
    };
});

// Относителният път в connection string-а се разрешава спрямо папката на проекта
// (иначе при стартиране от Visual Studio базата се създава в bin/Debug/...).
var connectionString = builder.Configuration.GetConnectionString("CelebriaDb")
    ?? "Data Source=App_Data/celebria.db";
var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString).DataSource;
if (!Path.IsPathRooted(dataSource))
{
    connectionString = $"Data Source={Path.Combine(builder.Environment.ContentRootPath, dataSource)}";
}

// SINGLETON №1 – базата данни. Цялото приложение ползва един и същ обект Database
// през интерфейса IEventDatabase.
builder.Services.AddSingleton<IEventDatabase>(_ => Database.Initialize(connectionString));
// Същата единствена инстанция обслужва и потребителите – през отделния интерфейс IUserDatabase.
builder.Services.AddSingleton<IUserDatabase>(sp => (Database)sp.GetRequiredService<IEventDatabase>());

// SINGLETON №2 – каталогът с видове събития. Регистрираме вече съществуващата
// единствена инстанция, за да може да се инжектира в контролери и изгледи (виж Services/EventTypeCatalog.cs).
builder.Services.AddSingleton(EventTypeCatalog.Instance);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();   // кой е потребителят (чете бисквитката)
app.UseAuthorization();    // има ли право ([Authorize])
app.UseRateLimiter();      // след автентикацията – лимитът е на потребител

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Създава базата (и примерните данни) при старт, а не при първата заявка.
app.Services.GetRequiredService<IEventDatabase>();

// Началните профили (администратор и тестов потребител) от секцията "SeedUsers".
// В проекта тя е само в appsettings.Development.json, така че извън Development не се създава нищо.
var seedUsers = app.Configuration.GetSection(SeedUserOptions.SectionName).Get<List<SeedUserOptions>>();
var createdUsers = UserSeeder.Seed(app.Services.GetRequiredService<IUserDatabase>(), seedUsers);
if (createdUsers > 0)
{
    app.Logger.LogInformation("Създадени начални профили: {Count}", createdUsers);
}

app.Run();
