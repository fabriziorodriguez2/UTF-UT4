using ProjectManager.Contracts;
using ProjectManager.Domain;
using ProjectManager.Interfaces;
using ProjectManager.Repositories;

namespace ProjectManager.Services;

/// <summary>
/// Lógica de negocio de proyectos (diagrama: ProjectService).
/// Depende de ProjectRepository (persiste/consulta) y de IUsuarios (valida dueño).
/// </summary>
public class ProjectService : IProyectos
{
    private readonly ProjectRepository _repository;
    private readonly IUsuarios _usuarios;

    public ProjectService(ProjectRepository repository, IUsuarios usuarios)
    {
        _repository = repository;
        _usuarios = usuarios;
    }

    public Project CreateProject(Guid ownerId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessRuleException("El nombre del proyecto no puede estar vacío.");

        // Arista "valida dueño": el usuario vive en UserManager, no en esta base.
        if (!_usuarios.Exist(ownerId))
            throw new BusinessRuleException($"El usuario {ownerId} no existe.");

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            OwnerId = ownerId,
            Status = ProjectStatuses.Open
        };

        _repository.Save(project);
        return project;
    }

    public Project GetById(Guid id) =>
        _repository.FindById(id)
        ?? throw new NotFoundException($"El proyecto {id} no existe.");

    public List<Project> GetProjectsByUser(Guid userId) =>
        _repository.FindByOwner(userId);

    /// <summary>Cerrar un proyecto ya cerrado es idempotente (no es error).</summary>
    public void CloseProject(Guid id)
    {
        var project = GetById(id);
        project.Status = ProjectStatuses.Closed;
        _repository.Save(project);
    }

    /// <summary>
    /// True solo si el proyecto existe y está abierto. Nunca lanza: lo consulta
    /// TaskManager antes de crear/mover tareas.
    /// </summary>
    public bool IsOpen(Guid id) =>
        _repository.FindById(id)?.Status == ProjectStatuses.Open;
}
