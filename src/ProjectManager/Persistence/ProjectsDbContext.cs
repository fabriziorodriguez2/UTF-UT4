using Microsoft.EntityFrameworkCore;
using ProjectManager.Domain;

namespace ProjectManager.Persistence;

/// <summary>
/// Contexto EF Core del componente ProjectManager → base PostgreSQL 'projects'.
/// </summary>
public class ProjectsDbContext : DbContext
{
    public ProjectsDbContext(DbContextOptions<ProjectsDbContext> options) : base(options) { }

    public DbSet<Project> Projects => Set<Project>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.OwnerId);
        });
    }
}
