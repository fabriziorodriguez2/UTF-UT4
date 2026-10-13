using TaskManager.Domain;

namespace TaskManager.Interfaces;

/// <summary>
/// Interfaz EXPUESTA por el componente TaskManager (diagrama: ITareas).
/// </summary>
public interface ITareas
{
    TaskItem CreateTask(Guid projectId, string title);
    TaskItem AssignTask(Guid taskId, Guid userId);
    TaskItem ChangeStatus(Guid taskId, string status);
    List<TaskItem> GetTasksByProject(Guid projectId);
}
