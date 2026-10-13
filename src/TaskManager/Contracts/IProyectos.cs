namespace TaskManager.Contracts;

/// <summary>
/// Interfaz CONSUMIDA de ProjectManager (arista "valida proyecto abierto").
/// Solo se declara lo que TaskManager necesita.
/// </summary>
public record ProjectDto(Guid Id, string Name, Guid OwnerId, string Status);

public interface IProyectos
{
    bool IsOpen(Guid projectId);
    ProjectDto? GetById(Guid projectId);
}
