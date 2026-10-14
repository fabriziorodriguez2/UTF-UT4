using TaskManager.Caching;

namespace TaskManager.Contracts;

/// <summary>
/// Decorador Cache-Aside de IProyectos: IsOpen se resuelve primero en Redis y
/// solo en un miss se llama a ProjectManager. GetById no se cachea.
/// </summary>
public class CachedProyectos : IProyectos
{
    private readonly ProyectosHttpClient _inner;
    private readonly CacheAside _cache;

    public CachedProyectos(ProyectosHttpClient inner, CacheAside cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public bool IsOpen(Guid projectId) =>
        _cache.GetOrLoad($"projects:{projectId}:is-open", () => _inner.IsOpen(projectId));

    public ProjectDto? GetById(Guid projectId) => _inner.GetById(projectId);
}
