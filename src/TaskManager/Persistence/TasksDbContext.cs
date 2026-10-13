using Microsoft.EntityFrameworkCore;
using TaskManager.Domain;

namespace TaskManager.Persistence;

/// <summary>
/// Contexto EF Core del componente TaskManager → base PostgreSQL 'tasks'.
/// Sobre esta base se abren las transacciones que dan la propiedad ACID
/// (crear + asignar una tarea de forma atómica).
/// </summary>
public class TasksDbContext : DbContext
{
    public TasksDbContext(DbContextOptions<TasksDbContext> options) : base(options) { }

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskItem>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.ProjectId);
            e.HasIndex(t => t.Assignee);
        });
    }
}
