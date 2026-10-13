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

var app = builder.Build();
// Verificar la configuración antes de aceptar solicitudes.
app.Services.GetRequiredService<ITokenValidator>();

app.MapGet("/health", () => Results.Ok("ok"));

// Todo lo demás pasa por el router (valida token + enruta/balancea).
app.MapFallback("/{**path}", async context =>
{
    var router = context.RequestServices.GetRequiredService<IApiRest>();
    await router.Route(context);
});

app.Run();
