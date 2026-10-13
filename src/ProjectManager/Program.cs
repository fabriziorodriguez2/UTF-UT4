using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProjectManager.Contracts;
using ProjectManager.Interfaces;
using ProjectManager.Middleware;
using ProjectManager.Persistence;
using ProjectManager.Repositories;
using ProjectManager.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Persistencia: base propia del componente (PostgreSQL) ────────────────────
builder.Services.AddDbContext<ProjectsDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Db")));

// ── Composición: interfaz expuesta y lógica de negocio ───────────────────────
builder.Services.AddScoped<ProjectRepository>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<IProyectos>(sp => sp.GetRequiredService<ProjectService>());

// ── Interfaz consumida IUsuarios → cliente HTTP hacia UserManager ────────────
// El AuthForwardingHandler reenvía el JWT del usuario: /users/{id}/exists exige token.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AuthForwardingHandler>();

builder.Services.AddHttpClient<IUsuarios, UsuariosHttpClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Users"]!))  // valida dueño
    .AddHttpMessageHandler<AuthForwardingHandler>();

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
    var db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
    db.Database.EnsureCreated();
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
