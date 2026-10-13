namespace TaskManager.Contracts;

/// <summary>
/// Interfaz CONSUMIDA de UserManager (arista "valida asignado").
/// TaskManager obtiene el usuario una vez al asignar para validar su existencia
/// y materializar su nombre junto a la tarea.
/// </summary>
public record UserDto(Guid Id, string Name, string Email);

public interface IUsuarios
{
    bool Exist(Guid id);
    UserDto? GetById(Guid id);
}
