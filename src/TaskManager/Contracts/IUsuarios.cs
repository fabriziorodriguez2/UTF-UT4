namespace TaskManager.Contracts;

/// <summary>
/// Interfaz CONSUMIDA de UserManager (arista "valida asignado").
/// TaskManager la usa para confirmar que el usuario asignado existe.
/// </summary>
public record UserDto(Guid Id, string Name, string Email);

public interface IUsuarios
{
    bool Exist(Guid id);
    UserDto? GetById(Guid id);
}
