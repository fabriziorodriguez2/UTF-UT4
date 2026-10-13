namespace TaskManager.Domain;

/// <summary>
/// Entidad Task del diagrama (TaskManager.dll).
///   Id : Guid, ProjectId : Guid, Assignee : Guid, Title : string, Status : string
///
/// Se llama TaskItem para no chocar con System.Threading.Tasks.Task.
/// </summary>
public class TaskItem
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }

    // Usuario asignado. Guid.Empty mientras la tarea no tiene responsable.
    public Guid Assignee { get; set; }

    public string Title { get; set; } = string.Empty;

    // Ver TaskStatuses para los valores válidos.
    public string Status { get; set; } = TaskStatuses.Todo;
}

/// <summary>Estados válidos de una tarea en el tablero.</summary>
public static class TaskStatuses
{
    public const string Todo = "Todo";
    public const string InProgress = "InProgress";
    public const string Done = "Done";

    public static readonly string[] All = { Todo, InProgress, Done };

    public static bool IsValid(string status) =>
        All.Contains(status, StringComparer.OrdinalIgnoreCase);
}
