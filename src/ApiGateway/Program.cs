using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using ApiGateway.Auth;
using ApiGateway.Interfaces;
using ApiGateway.Routing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("Gateway", client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        // Sin reutilización de conexión: cada request abre una conexión nueva y
        // vuelve a resolver el nombre de servicio por DNS, de modo que Docker
        // reparta entre las réplicas (balanceo horizontal visible en la demo).
        // Con keep-alive el socket queda "pegado" a una sola réplica.
        // En producción se usaría un balanceador L7 dedicado (p. ej. YARP con
        // round-robin, o un service mesh) manteniendo el pool de conexiones.
        PooledConnectionLifetime = TimeSpan.Zero
    });
builder.Services.AddSingleton<ITokenValidator, JwtTokenValidator>();
builder.Services.AddSingleton<RouteTable>();
builder.Services.AddSingleton<IApiRest, GatewayRouter>();

// ── Patrón GATEWAY OFFLOADING ────────────────────────────────────────────────
// El rate limiting (una responsabilidad transversal) se resuelve UNA sola vez en
// el gateway, en lugar de que cada componente lo implemente por su cuenta. Las
// requests que superan el límite reciben 429 ANTES de ser reenviadas, por lo que
// los servicios de negocio quedan protegidos de sobrecarga/abuso.
var permitLimit = builder.Configuration.GetValue("RateLimit:PermitLimit", 15);
var windowSeconds = builder.Configuration.GetValue("RateLimit:WindowSeconds", 10);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("gateway", o =>
    {
        o.PermitLimit = permitLimit;
        o.Window = TimeSpan.FromSeconds(windowSeconds);
        o.QueueLimit = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

var app = builder.Build();
// Verificar la configuración antes de aceptar solicitudes.
app.Services.GetRequiredService<ITokenValidator>();

app.UseRateLimiter();

// /health queda FUERA del rate limit (lo usan los healthchecks/monitoreo).
app.MapGet("/health", () => Results.Ok("ok"));

// Todo lo demás pasa por el router (valida token + enruta/balancea) y queda
// sujeto al rate limiting centralizado del gateway.
app.MapFallback("/{**path}", async context =>
{
    var router = context.RequestServices.GetRequiredService<IApiRest>();
    await router.Route(context);
}).RequireRateLimiting("gateway");

app.Run();
