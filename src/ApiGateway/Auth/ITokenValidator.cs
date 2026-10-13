namespace ApiGateway.Auth;

/// <summary>
/// En el diagrama, GatewayRouter tiene una dependencia 'autenticacion : IAutenticacion'
/// y la arista "valida token" hacia UserManager.
///
/// Como el sistema es de servicios SIN ESTADO, el gateway valida el JWT localmente
/// con el secreto compartido, sin llamar a UserManager en cada request. Esta
/// interfaz representa esa validación.
/// </summary>
public interface ITokenValidator
{
    /// <summary>Devuelve true si el JWT es válido (firma + expiración + issuer/audience).</summary>
    bool IsValid(string? bearerToken);
}
