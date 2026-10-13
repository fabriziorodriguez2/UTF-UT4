using ProjectManager.Domain;

namespace ProjectManager.Interfaces;

/// <summary>
/// Interfaz EXPUESTA por el componente ProjectManager (diagrama: IProyectos).
/// La consume TaskManager para validar que el proyecto esté abierto antes de
/// crear/mover tareas (arista "valida proyecto abierto").
/// </summary>
public interface IProyectos
{
    Project CreateProject(Guid ownerId, string name);
    Project GetById(Guid id);
    List<Project> GetProjectsByUser(Guid userId);
    void CloseProject(Guid id);
    bool IsOpen(Guid id);
}
