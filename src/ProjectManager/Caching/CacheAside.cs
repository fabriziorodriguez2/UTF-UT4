using Microsoft.Extensions.Caching.Distributed;

namespace ProjectManager.Caching;

/// <summary>
/// Patrón Cache-Aside sobre el caché distribuido (Redis), compartido por todas
/// las réplicas. Antes de ir a la fuente se consulta el caché: si el dato está
/// (hit) se usa; si no (miss) se hace la llamada real y se guarda con un TTL
/// corto que acota la inconsistencia.
/// El caché no es autoritativo: si Redis no responde se va directo a la fuente.
/// </summary>
public class CacheAside
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<CacheAside> _logger;
    private readonly DistributedCacheEntryOptions _options;

    public CacheAside(IDistributedCache cache, ILogger<CacheAside> logger, IConfiguration config)
    {
        _cache = cache;
        _logger = logger;
        var ttl = config.GetValue("Cache:TtlSeconds", 30);
        _options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(ttl)
        };
    }

    public bool GetOrLoad(string key, Func<bool> load)
    {
        try
        {
            var cached = _cache.GetString(key);
            if (cached is not null)
            {
                _logger.LogInformation("Cache HIT {Key}", key);
                return cached == "1";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Caché no disponible, consultando la fuente ({Key})", key);
            return load();
        }

        _logger.LogInformation("Cache MISS {Key}", key);
        var value = load();

        try
        {
            _cache.SetString(key, value ? "1" : "0", _options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo guardar en caché ({Key})", key);
        }

        return value;
    }
}
