namespace ApiGateway.Interfaces;

/// <summary>
/// Interfaz EXPUESTA por el componente ApiGateway (diagrama: IApiRest).
///   Route(request : HttpRequest) : HttpResponse
///
/// En ASP.NET usamos HttpContext (contiene el Request y el Response), que es la
/// forma idiomática de recibir la petición y escribir la respuesta reenviada.
/// </summary>
public interface IApiRest
{
    Task Route(HttpContext context);
}
