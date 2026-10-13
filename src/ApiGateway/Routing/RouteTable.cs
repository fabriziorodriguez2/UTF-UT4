namespace ApiGateway.Routing;

/// <summary>
/// Mapea el prefijo de la ruta entrante al componente de destino.
/// La URL destino es el NOMBRE DE SERVICIO de Docker, resuelto por su DNS interno.
///
///   /auth/**      → UserManager     (solo login y register son públicos)
///   /users/**     → UserManager
///   /projects/**  → ProjectManager
///   /tasks/**     → TaskManager
/// </summary>
public class RouteTable
{
    private readonly IConfiguration _config;

    public RouteTable(IConfiguration config) => _config = config;

    /// <summary>Base URL del componente destino, o null si la ruta no existe.</summary>
    public string? ResolveTarget(PathString path)
    {
        if (path.StartsWithSegments("/auth") || path.StartsWithSegments("/users"))
            return _config["Services:Users"];
        if (path.StartsWithSegments("/projects"))
            return _config["Services:Projects"];
        if (path.StartsWithSegments("/tasks"))
            return _config["Services:Tasks"];
        return null;
    }

    /// <summary>True si la ruta es pública (no exige JWT), p. ej. /auth/*.</summary>
    public bool IsPublic(PathString path)
    {
        var value = path.Value?.TrimEnd('/');
        return string.Equals(value, "/auth/register", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "/auth/login", StringComparison.OrdinalIgnoreCase);
    }
}
