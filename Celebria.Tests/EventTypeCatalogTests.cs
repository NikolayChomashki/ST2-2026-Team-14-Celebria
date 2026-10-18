using System.Reflection;
using Celebria.Services;

namespace Celebria.Tests;

/// <summary>Проверява, че EventTypeCatalog спазва правилата на Singleton.</summary>
public class EventTypeCatalogTests
{
    [Fact]
    public void Instance_ReturnsTheSameObjectEveryTime()
    {
        var a = EventTypeCatalog.Instance;
        var b = EventTypeCatalog.Instance;

        Assert.Same(a, b);
    }

    [Fact]
    public async Task Instance_IsCreatedOnlyOnce_EvenWithManyThreads()
    {
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => EventTypeCatalog.Instance))
            .ToArray();

        var instances = await Task.WhenAll(tasks);

        Assert.All(instances, i => Assert.Same(instances[0], i));
        Assert.Equal(1, EventTypeCatalog.CreatedInstances);
    }

    [Fact]
    public void Class_IsSealed_AndHasNoPublicConstructor()
    {
        var type = typeof(EventTypeCatalog);

        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Theory]
    [InlineData("wedding", true)]
    [InlineData("WEDDING", true)]   // кодовете не зависят от регистъра
    [InlineData("baptism", true)]
    [InlineData("funeral", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Exists_RecognisesOnlyKnownCodes(string? code, bool expected)
    {
        Assert.Equal(expected, EventTypeCatalog.Instance.Exists(code));
    }

    [Fact]
    public void NameOf_ReturnsBulgarianName()
    {
        Assert.Equal("Сватба", EventTypeCatalog.Instance.NameOf("wedding"));
        Assert.Equal("unknown", EventTypeCatalog.Instance.NameOf("unknown"));
    }

    [Fact]
    public void All_IsReadOnly()
    {
        var all = EventTypeCatalog.Instance.All;

        Assert.NotEmpty(all);
        Assert.Throws<NotSupportedException>(() => ((IList<Celebria.Models.EventType>)all).Add(new("x", "X")));
    }
}
