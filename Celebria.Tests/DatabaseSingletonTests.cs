using System.Reflection;
using Celebria.Data;

namespace Celebria.Tests;

/// <summary>Проверява Singleton поведението на Database (Initialize/Instance).</summary>
public class DatabaseSingletonTests
{
    [Fact]
    public void Initialize_ReturnsSameInstance_AndInstanceMatches()
    {
        var file = Path.Combine(Path.GetTempPath(), $"celebria-singleton-{Guid.NewGuid():N}.db");

        var first = Database.Initialize($"Data Source={file}");
        var second = Database.Initialize("Data Source=някой-друг-файл.db");   // игнорира се – вече има инстанция

        Assert.Same(first, second);
        Assert.Same(first, Database.Instance);
    }

    [Fact]
    public void Class_IsSealed_AndHasNoPublicConstructor()
    {
        var type = typeof(Database);

        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }
}
