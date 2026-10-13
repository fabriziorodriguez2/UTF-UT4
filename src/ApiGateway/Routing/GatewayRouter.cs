using ApiGateway.Auth;
using ApiGateway.Interfaces;
using Microsoft.AspNetCore.Http.Features;

namespace ApiGateway.Routing;

/// <summary>
/// Implementa IApiRest (diagrama: GatewayRouter).
/// Responsabilidades:
///   - "valida token": rechaza la request si el JWT no es válido (salvo rutas públicas).
///   - "enruta / balancea": reenvía la request al nombre de servicio Docker;
///     la selección de réplicas depende de DNS y de las conexiones disponibles.
/// </summary>
public class GatewayRouter : IApiRest
{
    // En el diagrama: 'autenticacion : IAutenticacion'. Aquí validamos el JWT
    // localmente (servicios sin estado) → ITokenValidator.
    private readonly ITokenValidator _autenticacion;
    private readonly RouteTable _routes;
    private readonly IHttpClientFactory _httpFactory;

    public GatewayRouter(
        ITokenValidator autenticacion,
        RouteTable routes,
        IHttpClientFactory httpFactory)
    {
        _autenticacion = autenticacion;
        _routes = routes;
        _httpFactory = httpFactory;
    }

    public async Task Route(HttpContext context)
    {
        var request = context.Request;
        var target = _routes.ResolveTarget(request.Path);
        if (target is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!_routes.IsPublic(request.Path) && !_autenticacion.IsValid(request.Headers.Authorization.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }

        using var outgoing = new HttpRequestMessage(new HttpMethod(request.Method),
            target.TrimEnd('/') + request.Path.ToUriComponent() + request.QueryString.ToUriComponent());
        var hasBody = context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody
            ?? (request.ContentLength > 0 || request.Headers.ContainsKey("Transfer-Encoding"));
        if (hasBody)
            outgoing.Content = new StreamContent(request.Body);

        var excludedRequestHeaders = ExcludedHeaders(request.Headers.Connection.ToString());
        excludedRequestHeaders.Add("Host");
        foreach (var header in request.Headers)
        {
            if (excludedRequestHeaders.Contains(header.Key)) continue;
            if (!outgoing.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                outgoing.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(100));
        try
        {
            using var client = _httpFactory.CreateClient("Gateway");
            using var response = await client.SendAsync(outgoing, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            context.Response.StatusCode = (int)response.StatusCode;
            var excludedResponseHeaders = ExcludedHeaders(string.Join(",", response.Headers.Connection));
            foreach (var header in response.Headers.Concat(response.Content.Headers))
            {
                if (!excludedResponseHeaders.Contains(header.Key))
                    context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            if (!HttpMethods.IsHead(request.Method))
                await response.Content.CopyToAsync(context.Response.Body, timeout.Token);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Abort();
        }
        catch (OperationCanceledException)
        {
            Fail(context, StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException)
        {
            Fail(context, StatusCodes.Status502BadGateway);
        }
        catch (IOException)
        {
            Fail(context, StatusCodes.Status502BadGateway);
        }
    }

    // Estas cabeceras pertenecen a una conexión, no al mensaje reenviado.
    private static HashSet<string> ExcludedHeaders(string connection)
    {
        var headers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
            "Proxy-Connection", "TE", "Trailer", "Transfer-Encoding", "Upgrade"
        };
        foreach (var name in connection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            headers.Add(name);
        return headers;
    }

    private static void Fail(HttpContext context, int status)
    {
        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }
        context.Response.Clear();
        context.Response.StatusCode = status;
    }
}
