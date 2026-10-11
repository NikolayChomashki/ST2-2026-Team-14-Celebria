using Celebria.Data;
using Celebria.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

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
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Events}/{action=Index}/{id?}");

// Създава базата (и примерните данни) при старт, а не при първата заявка.
app.Services.GetRequiredService<IEventDatabase>();

app.Run();
