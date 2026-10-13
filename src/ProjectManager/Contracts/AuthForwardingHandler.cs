using System.Net.Http.Headers;

namespace ProjectManager.Contracts;

/// <summary>
/// Copia el header "Authorization: Bearer &lt;token&gt;" del request entrante a
/// las llamadas salientes hacia UserManager. Como /users/{id}/exists exige JWT
/// ([Authorize]), sin esta propagación la validación del dueño daría 401.
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
