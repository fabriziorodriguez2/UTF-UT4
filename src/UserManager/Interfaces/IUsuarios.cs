using UserManager.Domain;

namespace UserManager.Interfaces;

/// <summary>
/// Interfaz EXPUESTA por el componente UserManager (diagrama: IUsuarios).
/// La consumen ProjectManager (valida dueño) y TaskManager (valida asignado).
/// </summary>
public interface IUsuarios
{
    User GetById(Guid id);
    List<User> GetAll();
    bool Exist(Guid id);
}
