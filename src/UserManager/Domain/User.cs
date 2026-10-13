namespace UserManager.Domain;

/// <summary>
/// Entidad User del diagrama de componentes (UserManager.dll).
///   Id : Guid, Name : string, Email : string
/// Se agrega PasswordHash como detalle de implementación de la autenticación
/// (no aparece en el diagrama conceptual, pero es necesario para IAutenticacion).
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // Detalle de implementación — nunca se expone hacia afuera.
    public string PasswordHash { get; set; } = string.Empty;
}
