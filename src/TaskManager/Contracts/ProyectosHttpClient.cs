using System.Net;
using System.Net.Http.Json;

namespace TaskManager.Contracts;

/// <summary>
/// Implementación de IProyectos que consume ProjectManager vía REST.
/// Las llamadas son síncronas para respetar la interfaz del diagrama; en ASP.NET
/// Core no hay SynchronizationContext, por lo que el bloqueo es seguro.
/// </summary>
public class ProyectosHttpClient : IProyectos
{
    private readonly HttpClient _http;

    public ProyectosHttpClient(HttpClient http) => _http = http;

    public bool IsOpen(Guid projectId)
    {
        var resp = _http.GetAsync($"/projects/{projectId}/is-open")
                        .GetAwaiter().GetResult();

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return false;

        resp.EnsureSuccessStatusCode();
        return resp.Content.ReadFromJsonAsync<bool>().GetAwaiter().GetResult();
    }

    public ProjectDto? GetById(Guid projectId)
    {
        var resp = _http.GetAsync($"/projects/{projectId}")
                        .GetAwaiter().GetResult();

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        resp.EnsureSuccessStatusCode();
        return resp.Content.ReadFromJsonAsync<ProjectDto>().GetAwaiter().GetResult();
    }
}
