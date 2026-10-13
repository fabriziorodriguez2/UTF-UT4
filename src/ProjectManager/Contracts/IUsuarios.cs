namespace ProjectManager.Contracts;

/// <summary>
/// Interfaz CONSUMIDA de UserManager (arista "valida dueño" del diagrama).
/// Aquí solo se declara lo que ProjectManager necesita; la implementación real
/// (UsuariosHttpClient) llama por HTTP al componente UserManager.
/// UserDto es la vista mínima del usuario que este componente necesita.
/// </summary>
public record UserDto(Guid Id, string Name, string Email);

public interface IUsuarios
{
    bool Exist(Guid id);
    UserDto? GetById(Guid id);
}
