using Celebria.Models;

namespace Celebria.Data;

/// <summary>
/// Договор за достъп до потребителите (Repository). Отделен е от <see cref="IEventDatabase"/>
/// (Interface Segregation – буквата I от SOLID): AccountController и AdminController не трябва да знаят за събитията.
/// </summary>
public interface IUserDatabase
{
    /// <summary>INSERT – добавя потребител и връща новото Id.</summary>
    int AddUser(User user);

    /// <summary>Търси по потребителско име, без значение от главни/малки букви.</summary>
    User? GetUserByUsername(string username);

    User? GetUserById(int id);

    /// <summary>Всички потребители – първо администраторите, после по име.</summary>
    IReadOnlyList<User> GetAllUsers();

    bool UsernameExists(string username);

    /// <summary>Сменя ролята. Връща false, ако няма такъв потребител.</summary>
    bool SetRole(int userId, string role);

    /// <summary>Изтрива потребителя. Връща false, ако няма такъв потребител.</summary>
    bool DeleteUser(int userId);

    int CountAdmins();
}
