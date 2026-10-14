using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TaskManager.Caching;
using TaskManager.Contracts;
using TaskManager.Interfaces;
using TaskManager.Middleware;
using TaskManager.Persistence;
using TaskManager.Repositories;
using TaskManager.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Persistencia: base propia del componente (PostgreSQL) ────────────────────
builder.Services.AddDbContext<TasksDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Db")));

// ── Composición: interfaz expuesta y lógica de negocio ───────────────────────
builder.Services.AddScoped<TaskRepository>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<ITareas>(sp => sp.GetRequiredService<TaskService>());

// ── Interfaces consumidas → clientes HTTP hacia los otros componentes ────────
// El AuthForwardingHandler reenvía el JWT del usuario a esas llamadas.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AuthForwardingHandler>();

builder.Services.AddHttpClient<ProyectosHttpClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Projects"]!)) // valida proyecto abierto
    .AddHttpMessageHandler<AuthForwardingHandler>();

builder.Services.AddHttpClient<UsuariosHttpClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Users"]!))    // valida asignado
    .AddHttpMessageHandler<AuthForwardingHandler>();

// ── Cache-Aside: Redis compartido por todas las réplicas ─────────────────────
// Las interfaces consumidas se decoran: primero el caché, en un miss la llamada HTTP.
builder.Services.AddStackExchangeRedisCache(opt =>
{
    opt.Configuration = builder.Configuration.GetConnectionString("Redis");
    opt.InstanceName = "mgp:";
});
builder.Services.AddSingleton<CacheAside>();
builder.Services.AddScoped<IProyectos, CachedProyectos>();
builder.Services.AddScoped<IUsuarios, CachedUsuarios>();

// ── Autenticación stateless: validación local del JWT con el secreto ─────────
var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!)),
            ValidateLifetime = true
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Para la demo: crea el esquema si no existe (en producción se usarían migraciones).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
    db.Database.EnsureCreated();
    // EnsureCreated no actualiza tablas existentes. Esta ampliación de la demo
    // conserva los datos y también se puede ejecutar al reiniciar cada réplica.
    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Tasks" ADD COLUMN IF NOT EXISTS "AssigneeName" text NULL;
        """);
}

// Identifica qué réplica respondió (demo de escalabilidad horizontal).
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Served-By"] = Environment.MachineName;
    await next();
});

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok("ok"));

app.Run();
