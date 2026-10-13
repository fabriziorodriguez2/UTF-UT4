using ProjectManager.Domain;

namespace ProjectManager.Middleware;

/// <summary>
/// Traduce las excepciones de dominio a respuestas HTTP con ProblemDetails:
///   NotFoundException     → 404 (lo espera ProyectosHttpClient de TaskManager)
///   BusinessRuleException → 409 (regla de negocio: dueño inexistente, etc.)
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (NotFoundException ex)
        {
            await Write(context, StatusCodes.Status404NotFound, ex.Message);
        }
        catch (BusinessRuleException ex)
        {
            await Write(context, StatusCodes.Status409Conflict, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error no controlado");
            await Write(context, StatusCodes.Status500InternalServerError, "Error interno.");
        }
    }

    private static Task Write(HttpContext context, int status, string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { status, detail });
    }
}
