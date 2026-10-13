using ProjectManager.Domain;
using ProjectManager.Persistence;

namespace ProjectManager.Repositories;

/// <summary>
/// Persistencia de proyectos (diagrama: ProjectRepository).
///   Save / FindById / FindByOwner
/// </summary>
public class ProjectRepository
{
    private readonly ProjectsDbContext _db;

    public ProjectRepository(ProjectsDbContext db) => _db = db;

    /// <summary>Inserta o actualiza el proyecto y persiste el cambio.</summary>
    public void Save(Project project)
    {
        var existing = _db.Projects.Find(project.Id);
        if (existing is null)
            _db.Projects.Add(project);
        else
            _db.Entry(existing).CurrentValues.SetValues(project);

        _db.SaveChanges();
    }

    public Project? FindById(Guid id) => _db.Projects.Find(id);

    public List<Project> FindByOwner(Guid ownerId) =>
        _db.Projects.Where(p => p.OwnerId == ownerId).ToList();
}
