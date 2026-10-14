using TaskManager.Caching;

namespace TaskManager.Contracts;

/// <summary>
/// Decorador Cache-Aside de IUsuarios: Exist se resuelve primero en Redis y solo
/// en un miss se llama a UserManager. GetById no se cachea.
/// </summary>
public class CachedUsuarios : IUsuarios
{
    private readonly UsuariosHttpClient _inner;
    private readonly CacheAside _cache;

    public CachedUsuarios(UsuariosHttpClient inner, CacheAside cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public bool Exist(Guid id) =>
        _cache.GetOrLoad($"users:{id}:exists", () => _inner.Exist(id));

    public UserDto? GetById(Guid id) => _inner.GetById(id);
}
