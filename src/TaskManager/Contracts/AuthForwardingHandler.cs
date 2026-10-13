using System.Net.Http.Headers;

namespace TaskManager.Contracts;

/// <summary>
/// Copia el header "Authorization: Bearer &lt;token&gt;" del request entrante a
/// las llamadas salientes hacia ProjectManager / UserManager. Como esos
/// componentes exigen JWT ([Authorize]), hay que propagar el token del usuario.
/// </summary>
public class AuthForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _accessor;

    public AuthForwardingHandler(IHttpContextAccessor accessor) => _accessor = accessor;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var incoming = _accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(incoming) &&
            AuthenticationHeaderValue.TryParse(incoming, out var header))
        {
            request.Headers.Authorization = header;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
