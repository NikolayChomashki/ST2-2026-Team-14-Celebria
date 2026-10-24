using Microsoft.Data.Sqlite;
using Celebria.Data;
using Celebria.Models;
using Celebria.Services;

namespace Celebria.Tests;

/// <summary>Тестове за хеширането на пароли.</summary>
public class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_CorrectPassword_ReturnsTrue()
    {
        var hash = PasswordHasher.Hash("Celebria2026!");

        Assert.True(PasswordHasher.Verify("Celebria2026!", hash));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = PasswordHasher.Hash("Celebria2026!");

        Assert.False(PasswordHasher.Verify("celebria2026!", hash));
        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void Hash_SamePasswordTwice_GivesDifferentHashes_BecauseOfRandomSalt()
    {
        Assert.NotEqual(PasswordHasher.Hash("Celebria2026!"), PasswordHasher.Hash("Celebria2026!"));
    }

    [Fact]
    public void Hash_DoesNotContainThePassword()
    {
        Assert.DoesNotContain("Celebria2026!", PasswordHasher.Hash("Celebria2026!"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("v1.abc.%%%.%%%")]
    [InlineData("v2.100000.AAAA.AAAA")]
    public void Verify_MalformedHash_ReturnsFalse(string storedHash)
    {
        Assert.False(PasswordHasher.Verify("Celebria2026!", storedHash));
    }
}

/// <summary>Интеграционни тестове за таблица Users върху временна SQLite база.</summary>
public sealed class UserDatabaseTests : IDisposable
{
    private readonly string _file;
    private readonly Database _db;

    public UserDatabaseTests()
    {
        _file = Path.Combine(Path.GetTempPath(), $"celebria-users-{Guid.NewGuid():N}.db");
        _db = Database.CreateIsolated($"Data Source={_file}");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }

    private static User Sample(string username = "maria.ivanova") => new()
    {
        Username = username,
        DisplayName = "Мария Иванова",
        PasswordHash = PasswordHasher.Hash("Celebria2026!")
    };

    [Fact]
    public void AddUser_ThenGetByUsername_ReturnsSameUser()
    {
        var id = _db.AddUser(Sample());

        var loaded = _db.GetUserByUsername("maria.ivanova");

        Assert.NotNull(loaded);
        Assert.Equal(id, loaded.Id);
        Assert.Equal("Мария Иванова", loaded.DisplayName);
        Assert.True(PasswordHasher.Verify("Celebria2026!", loaded.PasswordHash));
    }

    [Fact]
    public void GetUserByUsername_IgnoresCaseAndSpaces()
    {
        _db.AddUser(Sample("Maria.Ivanova"));

        Assert.NotNull(_db.GetUserByUsername("  maria.IVANOVA "));
        Assert.True(_db.UsernameExists("MARIA.ivanova"));
    }

    [Fact]
    public void GetUserByUsername_Unknown_ReturnsNull()
    {
        Assert.Null(_db.GetUserByUsername("nobody"));
        Assert.False(_db.UsernameExists("nobody"));
    }

    [Fact]
    public void AddUser_DuplicateUsername_IsRejectedByUniqueConstraint()
    {
        _db.AddUser(Sample("maria.ivanova"));

        var ex = Assert.Throws<SqliteException>(() => _db.AddUser(Sample("MARIA.IVANOVA")));
        Assert.Equal(19, ex.SqliteErrorCode);   // SQLITE_CONSTRAINT
    }

    // ------------------------------------------------------------ роли и админ панел

    [Fact]
    public void AddUser_DefaultRole_IsUser()
    {
        var id = _db.AddUser(Sample());

        var loaded = _db.GetUserById(id)!;
        Assert.Equal(Roles.User, loaded.Role);
        Assert.False(loaded.IsAdmin);
        Assert.Equal(0, _db.CountAdmins());
    }

    [Fact]
    public void SetRole_ChangesRole_AndCountAdmins()
    {
        var id = _db.AddUser(Sample());

        Assert.True(_db.SetRole(id, Roles.Admin));
        Assert.True(_db.GetUserById(id)!.IsAdmin);
        Assert.Equal(1, _db.CountAdmins());

        Assert.True(_db.SetRole(id, Roles.User));
        Assert.Equal(0, _db.CountAdmins());
    }

    [Fact]
    public void SetRole_UnknownRoleText_BecomesUser_NeverAdmin()
    {
        var id = _db.AddUser(Sample());

        _db.SetRole(id, "superadmin");

        Assert.Equal(Roles.User, _db.GetUserById(id)!.Role);
    }

    [Fact]
    public void DeleteUser_RemovesOnlyThatUser()
    {
        var keep = _db.AddUser(Sample("keep.me"));
        var remove = _db.AddUser(Sample("remove.me"));

        Assert.True(_db.DeleteUser(remove));

        Assert.Null(_db.GetUserById(remove));
        Assert.NotNull(_db.GetUserById(keep));
        Assert.False(_db.DeleteUser(remove));
    }

    [Fact]
    public void GetAllUsers_ListsAdminsFirst()
    {
        _db.AddUser(Sample("zara"));
        var admin = Sample("bob");
        admin.Role = Roles.Admin;
        _db.AddUser(admin);
        _db.AddUser(Sample("alice"));

        var all = _db.GetAllUsers();

        Assert.Equal(new[] { "bob", "alice", "zara" }, all.Select(u => u.Username));
    }

    // ------------------------------------------------------------ начални профили (SeedUsers)

    private static List<SeedUserOptions> Seeds() => new()
    {
        new() { Username = "admin", DisplayName = "Администратор", Password = "Admin-Pass-123", Role = "Admin" },
        new() { Username = "test.user", DisplayName = "Тестов Потребител", Password = "User-Pass-123", Role = "User" }
    };

    [Fact]
    public void Seed_CreatesAdminAndTestUser_WithWorkingPasswords()
    {
        var created = UserSeeder.Seed(_db, Seeds());

        Assert.Equal(2, created);
        var admin = _db.GetUserByUsername("admin")!;
        var user = _db.GetUserByUsername("test.user")!;
        Assert.True(admin.IsAdmin);
        Assert.False(user.IsAdmin);
        Assert.True(PasswordHasher.Verify("Admin-Pass-123", admin.PasswordHash));
        Assert.True(PasswordHasher.Verify("User-Pass-123", user.PasswordHash));
    }

    [Fact]
    public void Seed_RunTwice_DoesNotDuplicateOrOverwrite()
    {
        UserSeeder.Seed(_db, Seeds());
        var changed = Seeds();
        changed[0].Password = "Different-Pass-456";

        var createdSecondTime = UserSeeder.Seed(_db, changed);

        Assert.Equal(0, createdSecondTime);
        Assert.Equal(2, _db.GetAllUsers().Count);
        Assert.True(PasswordHasher.Verify("Admin-Pass-123", _db.GetUserByUsername("admin")!.PasswordHash));
    }

    [Fact]
    public void Seed_NullOrIncompleteEntries_AreSkipped()
    {
        Assert.Equal(0, UserSeeder.Seed(_db, null));
        Assert.Equal(0, UserSeeder.Seed(_db, new[] { new SeedUserOptions { Username = "no.password" } }));
        Assert.Empty(_db.GetAllUsers());
    }
}
