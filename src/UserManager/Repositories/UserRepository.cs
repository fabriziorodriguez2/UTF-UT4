using Microsoft.EntityFrameworkCore;
using Npgsql;
using UserManager.Domain;
using UserManager.Persistence;

namespace UserManager.Repositories;

public class UserRepository
{
    private readonly UsersDbContext _db;

    public UserRepository(UsersDbContext db) => _db = db;

    public void Save(User user)
    {
        var existingUser = _db.Users.Find(user.Id);
        if (existingUser == null)
            _db.Users.Add(user);
        else
            _db.Entry(existingUser).CurrentValues.SetValues(user);

        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
            postgres.ConstraintName == "IX_Users_Email")
        {
            // También protege si dos registros con el mismo email llegan a la vez.
            throw new InvalidOperationException("Ya existe un usuario con ese email.", ex);
        }
    }

    public User? FindById(Guid id)
    {
        return _db.Users.Find(id);
    }

    public User? FindByEmail(string email)
    {
        return _db.Users.FirstOrDefault(user => user.Email == email);
    }

    public List<User> GetAll()
    {
        return _db.Users.AsNoTracking().OrderBy(user => user.Name).ThenBy(user => user.Id).ToList();
    }
}
