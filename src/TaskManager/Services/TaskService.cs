using TaskManager.Contracts;
using TaskManager.Domain;
using TaskManager.Interfaces;
using TaskManager.Persistence;
using TaskManager.Repositories;

namespace TaskManager.Services;

/// <summary>
/// Lógica de negocio de tareas (diagrama: TaskService).
/// Depende de TaskRepository (persiste/consulta), IProyectos (valida proyecto
/// abierto) e IUsuarios (valida asignado).
///
/// Aquí se demuestra la propiedad ACID: crear una tarea y asignarla debe ser
/// atómico — si algo falla, se deshace todo (rollback de la transacción).
/// </summary>
public class TaskService : ITareas
{
    private readonly TaskRepository _repository;
    private readonly IProyectos _proyectos;
    private readonly IUsuarios _usuarios;
    private readonly TasksDbContext _db; // para abrir transacciones explícitas

    public TaskService(
        TaskRepository repository,
        IProyectos proyectos,
        IUsuarios usuarios,
        TasksDbContext db)
    {
        _repository = repository;
        _proyectos = proyectos;
        _usuarios = usuarios;
        _db = db;
    }

    public TaskItem CreateTask(Guid projectId, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new BusinessRuleException("El título de la tarea no puede estar vacío.");

        // Arista "valida proyecto abierto".
        if (!_proyectos.IsOpen(projectId))
            throw new BusinessRuleException($"El proyecto {projectId} no existe o está cerrado.");

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Title = title,
            Status = TaskStatuses.Todo,
            Assignee = Guid.Empty
        };

        _repository.Save(task);
        return task;
    }

    public TaskItem AssignTask(Guid taskId, Guid userId)
    {
        // Arista "valida asignado".
        if (!_usuarios.Exist(userId))
            throw new BusinessRuleException($"El usuario {userId} no existe.");

        var task = _repository.FindById(taskId)
                   ?? throw new NotFoundException($"La tarea {taskId} no existe.");

        task.Assignee = userId;
        _repository.Save(task);
        return task;
    }

    /// <summary>
    /// Ejemplo del enunciado: "si se crea la tarea y se la asigna a un usuario,
    /// ambas operaciones deben realizarse; si alguna falla, se deshacen todos los
    /// cambios". Crear + asignar como UNA sola transacción atómica.
    /// </summary>
    public TaskItem CreateAndAssign(Guid projectId, string title, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new BusinessRuleException("El título de la tarea no puede estar vacío.");

        // Todas las escrituras dentro de una transacción: o se confirman todas, o
        // ninguna (atomicidad). El rollback se dispara ante cualquier excepción.
        using var tx = _db.Database.BeginTransaction();
        try
        {
            if (!_proyectos.IsOpen(projectId))
                throw new BusinessRuleException($"El proyecto {projectId} no existe o está cerrado.");

            if (!_usuarios.Exist(userId))
                throw new BusinessRuleException($"El usuario {userId} no existe.");

            var task = new TaskItem
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Title = title,
                Status = TaskStatuses.Todo,
                Assignee = userId
            };

            _db.Tasks.Add(task);
            _db.SaveChanges();   // persiste creación + asignación juntas
            tx.Commit();         // confirma la transacción (durabilidad)
            return task;
        }
        catch
        {
            tx.Rollback();       // deshace todo si algo falló
            throw;
        }
    }

    public TaskItem ChangeStatus(Guid taskId, string status)
    {
        if (!TaskStatuses.IsValid(status))
            throw new BusinessRuleException(
                $"Estado inválido '{status}'. Válidos: {string.Join(", ", TaskStatuses.All)}.");

        // Transacción para aislar cambios concurrentes de estado (aislamiento):
        // dos usuarios moviendo la misma tarea no deben pisarse.
        using var tx = _db.Database.BeginTransaction();
        try
        {
            var task = _repository.FindById(taskId)
                       ?? throw new NotFoundException($"La tarea {taskId} no existe.");

            task.Status = status;
            _repository.Save(task);
            tx.Commit();
            return task;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public List<TaskItem> GetTasksByProject(Guid projectId) =>
        _repository.FindByProject(projectId);

    /// <summary>Busca una tarea puntual (no está en ITareas; lo usa el endpoint GET /tasks/{id}).</summary>
    public TaskItem GetById(Guid taskId) =>
        _repository.FindById(taskId)
        ?? throw new NotFoundException($"La tarea {taskId} no existe.");
}
