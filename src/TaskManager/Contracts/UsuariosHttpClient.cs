using System.Net;
using System.Net.Http.Json;

namespace TaskManager.Contracts;

/// <summary>
/// Implementación de IUsuarios que consume UserManager vía REST.
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
