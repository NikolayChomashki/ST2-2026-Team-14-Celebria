# Celebria – Celebrate Moments

*Платформа за организиране на събития с ИИ асистент.*

**Celebria** е уеб платформа за агенции, които организират събития: сватби, кръщенета, рождени дни, фирмени партита, балове и конференции. Организаторът поддържа списък със събития и клиенти и ги филтрира. В следващите етапи ще има и ИИ асистент, който предлага програма, бюджет и идеи за декорация.

Курсов проект по **Софтуерни технологии 2**, ПУ „Паисий Хилендарски“, ФМИ, 2026/2027.

## Екип

| Член | GitHub | Роля |
|---|---|---|
| Николай Чомашки | `@...` | |
| Валентина Слабакова | `@...` | |
| Никол Георгиева | `@...` | |

## Технологии

| Част | Технология |
|---|---|
| Език / платформа | C# 12, .NET 8 (LTS) |
| Уеб рамка | ASP.NET Core MVC |
| База данни | SQLite (файл `Celebria/App_Data/celebria.db`) |
| Достъп до данните | Microsoft.Data.Sqlite (ADO.NET, параметризирани заявки) |
| Изгледи | Razor, Bootstrap 5, jQuery Validation |
| Тестове | xUnit |

## Стартиране

**Visual Studio 2022** (17.8+, workload „ASP.NET and web development“):
1. Отворете `Celebria.sln`.
2. Изберете проекта `Celebria` като Startup Project и натиснете **F5**.

**Команден ред** (нужен е [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)):

```bash
dotnet run --project Celebria      # стартира сайта
dotnet test                      # пуска всички тестове
```

При първо стартиране автоматично се създава `Celebria/App_Data/celebria.db` с 12 примерни събития. Ако изтриете файла, базата започва наново.

## Структура

```
ST2-2026-Team-X-Celebria/
├── Celebria.sln
├── Celebria/                          ← уеб приложението (MVC)
│   ├── Program.cs                   ← DI регистрация на двата Singleton-а, маршрути
│   ├── Controllers/
│   │   └── EventsController.cs      ← Index (SELECT + филтър), Create (INSERT), Details
│   ├── Models/
│   │   ├── Event.cs                 ← модел + валидации
│   │   ├── EventStatus.cs           ← Планирано / Потвърдено / Проведено / Отказано
│   │   ├── EventType.cs             ← неизменяем record за вид събитие
│   │   ├── EventFilter.cs           ← критерии за филтриране
│   │   └── EventIndexViewModel.cs
│   ├── Data/
│   │   ├── IEventDatabase.cs        ← интерфейс за достъп до данните
│   │   └── Database.cs              ← ★ SINGLETON (double-checked locking) върху SQLite
│   ├── Services/
│   │   └── EventTypeCatalog.cs      ← ★ SINGLETON (Lazy<T>) – каталог с видове събития
│   ├── Validation/
│   │   └── KnownEventTypeAttribute.cs
│   ├── Views/Events/                ← Index, Create, Details, _EventForm
│   └── wwwroot/
└── Celebria.Tests/                    ← xUnit тестове
```

## Седмица 1: какво е готово

- [x] **INSERT.** Формата `Events/Create` добавя събитие в таблица `Events`. Има валидация в браузъра (jQuery Validation) и на сървъра (DataAnnotations, `[KnownEventType]`, дата не в миналото).
- [x] **SELECT с филтър.** На `Events/Index` събитията се филтрират по текст (наименование, клиент, място), тип, статус и период от/до. Филтрирането се прави в SQL (`WHERE`), не в паметта. Критериите стоят в адреса (`/Events?type=wedding&status=Confirmed`), така че резултатът може да се сподели. Има и бързи филтри по тип.
- [x] **Singleton Design Pattern**, в два варианта (виж по-долу).
- [x] Unit и интеграционни тестове (xUnit).

## Design Patterns

### 1. Singleton: `Data/Database.cs`

**Проблем.** Всички контролери трябва да работят с една и съща база и конфигурация. Схемата трябва да се създаде и да се напълни с примерни данни точно веднъж.

**Реализация** (като в ръководството ContactsMVC):
- `sealed` клас и `private` конструктор, за да не може да се създаде инстанция с `new` отвън;
- `private static Database? _instance` пази единствената инстанция;
- `Initialize(connectionString)` създава инстанцията thread-safe с **double-checked locking**, а `Instance` дава достъп до нея;
- в `Program.cs` е регистриран като `AddSingleton<IEventDatabase>(_ => Database.Initialize(cs))`. Контролерите зависят само от интерфейса `IEventDatabase`, което е Dependency Inversion.

Класът не пази отворена връзка: всеки метод отваря собствена `SqliteConnection` (с connection pooling). Затова една споделена инстанция може безопасно да обслужва паралелни заявки.

### 2. Singleton: `Services/EventTypeCatalog.cs`

**Проблем.** Видовете събития („Сватба“, „Кръщене“…) трябва да са едни и същи във формата, филтъра, валидацията и списъка.

**Реализация:**
- класическата GoF форма с `private static readonly Lazy<EventTypeCatalog>` и `LazyThreadSafetyMode.ExecutionAndPublication`. Това е по-кратък еквивалент на double-checked locking;
- данните са неизменяеми (`record`, `FrozenDictionary`, `ReadOnlyCollection`), така че инстанцията се споделя без `lock`;
- ползва се по два начина: директно през `EventTypeCatalog.Instance` (във валидационния атрибут, който не може да получава DI зависимости) и през DI (`AddSingleton(EventTypeCatalog.Instance)` → контролер и `@inject` в изгледите).

### Предимства и недостатъци на Singleton, и как ги адресираме

| Недостатък | Как го смекчаваме |
|---|---|
| Глобално състояние и скрити зависимости | Контролерите получават `IEventDatabase` и `EventTypeCatalog` през конструктора, а не с `.Instance` |
| Трудно тестване | `Database.CreateIsolated(...)` е `internal` и се вижда само от `Celebria.Tests` (`InternalsVisibleTo`), така че всеки тест има собствена временна база |
| Проблеми при много нишки | Double-checked locking и `Lazy<T>`. Каталогът е неизменяем, а базата не държи споделена връзка |

### Следващи патърни (по плана на курса)

| Седмица | Патърн | Къде във Celebria |
|---|---|---|
| 2 | Factory + Strategy | смяна на ИИ доставчика (локален Ollama / отдалечен API) и на механизма за логване |
| 3 | Observer | известия в UI: ново събитие, наближаваща дата, промяна на статус |
| 4 | Repository + EF Core | замяна на `Database` с EF Core зад същия интерфейс |

## Тестове

```bash
dotnet test
```

| Файл | Какво проверява |
|---|---|
| `EventTypeCatalogTests` | една инстанция (и при 50 паралелни нишки), `sealed`, няма публичен конструктор, неизменяеми данни |
| `DatabaseSingletonTests` | `Initialize` винаги връща същия обект |
| `DatabaseTests` | INSERT/SELECT върху истинска SQLite база: филтри по тип, статус, период, търсене на кирилица без значение от регистъра, екраниране на `%` и `_`, защита от SQL injection |
| `EventValidationTests` | валидациите на модела `Event` |

## Използване на ИИ

Началната структура на проекта, класовете за Singleton и тестовете са генерирани с помощта на **Claude (Anthropic)** и след това са прегледани от екипа. Това е отбелязано и в съответните commit съобщения, според изискването на курса.
