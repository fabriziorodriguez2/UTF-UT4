using Microsoft.EntityFrameworkCore;
using UserManager.Domain;

namespace UserManager.Persistence;

/// <summary>
/// Contexto EF Core del componente UserManager → base PostgreSQL 'users'.
/// Cada componente tiene su propia base (aislamiento de datos, propiedad ACID).
/// </summary>
public class UsersDbContext : DbContext
{
    public UsersDbContext(DbContextOptions<UsersDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Id identifica al usuario; Email no se puede repetir.
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
        });
    }
}
