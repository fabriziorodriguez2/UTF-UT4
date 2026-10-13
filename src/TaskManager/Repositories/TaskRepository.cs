using Microsoft.EntityFrameworkCore;
using TaskManager.Domain;
using TaskManager.Persistence;

namespace TaskManager.Repositories;

/// <summary>
/// Persistencia de tareas (diagrama: TaskRepository).
///   Save / FindById / FindByProject / CountInProjectByUser
/// </summary>
public class TaskRepository
{
    private readonly TasksDbContext _db;

    public TaskRepository(TasksDbContext db) => _db = db;

    /// <summary>Inserta o actualiza la tarea y persiste el cambio.</summary>
    public void Save(TaskItem task)
    {
        var existing = _db.Tasks.Find(task.Id);
        if (existing is null)
            _db.Tasks.Add(task);
        else
            _db.Entry(existing).CurrentValues.SetValues(task);

        _db.SaveChanges();
    }

    public TaskItem? FindById(Guid id) => _db.Tasks.Find(id);

    public List<TaskItem> FindByProject(Guid projectId) =>
        _db.Tasks.Where(t => t.ProjectId == projectId).ToList();

    /// <summary>Cantidad de tareas asignadas a un usuario (carga de trabajo).</summary>
    public int CountInProjectByUser(Guid userId) =>
        _db.Tasks.Count(t => t.Assignee == userId);
}
