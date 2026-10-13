using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UserManager.Auth;
using UserManager.Interfaces;
using UserManager.Persistence;
using UserManager.Repositories;
using UserManager.Services;

var builder = WebApplication.CreateBuilder(args);

// Fallar al iniciar con un mensaje claro si falta la configuración del JWT.
var secret = builder.Configuration["Jwt:Secret"];
var issuer = builder.Configuration["Jwt:Issuer"];
var audience = builder.Configuration["Jwt:Audience"];
if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
    throw new InvalidOperationException("Jwt:Secret debe tener al menos 32 bytes UTF-8.");
if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
    throw new InvalidOperationException("Se deben configurar Jwt:Issuer y Jwt:Audience.");

builder.Services.AddDbContext<UsersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Db"),
        postgres => postgres.EnableRetryOnFailure()));

builder.Services.AddScoped<UserRepository>();
builder.Services.AddSingleton<JwtTokenIssuer>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<IUsuarios>(services => services.GetRequiredService<UserService>());
builder.Services.AddScoped<IAutenticacion>(services => services.GetRequiredService<UserService>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Para la entrega: crea la base y las tablas si todavía no existen.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok("ok"));
app.Run();
