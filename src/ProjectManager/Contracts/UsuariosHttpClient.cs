using System.Net;
using System.Net.Http.Json;

namespace ProjectManager.Contracts;

/// <summary>
/// Implementación de IUsuarios que consume el componente UserManager vía REST.
/// La URL base (Services:Users) apunta al nombre de servicio de Docker, por lo
/// que el DNS interno reparte entre las réplicas de UserManager.
/// Las llamadas son síncronas para respetar la interfaz del diagrama; en ASP.NET
/// Core no hay SynchronizationContext, por lo que el bloqueo es seguro.
/// </summary>
public class UsuariosHttpClient : IUsuarios
{
    private readonly HttpClient _http;

    public UsuariosHttpClient(HttpClient http) => _http = http;

    public bool Exist(Guid id)
    {
        var resp = _http.GetAsync($"/users/{id}/exists").GetAwaiter().GetResult();

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return false;

        resp.EnsureSuccessStatusCode();
        return resp.Content.ReadFromJsonAsync<bool>().GetAwaiter().GetResult();
    }

    public UserDto? GetById(Guid id)
    {
        var resp = _http.GetAsync($"/users/{id}").GetAwaiter().GetResult();

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        resp.EnsureSuccessStatusCode();
        return resp.Content.ReadFromJsonAsync<UserDto>().GetAwaiter().GetResult();
    }
}
