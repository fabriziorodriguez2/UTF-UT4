namespace UserManager.Interfaces;

/// <summary>
/// Interfaz EXPUESTA por el componente UserManager (diagrama: IAutenticacion).
/// La consume el ApiGateway para validar credenciales / emitir el token.
/// Ambos métodos devuelven el JWT (string) que habilita el resto de las llamadas.
/// </summary>
public interface IAutenticacion
{
    string Register(string name, string email, string password);
    string Authenticate(string email, string password);
}
