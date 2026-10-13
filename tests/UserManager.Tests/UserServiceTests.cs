using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using UserManager.Auth;
using UserManager.Controllers;
using UserManager.Persistence;
using UserManager.Repositories;
using UserManager.Services;
using Xunit;

namespace UserManager.Tests;

// SQLite permite probar consultas reales sin depender de Docker.
// Estas pruebas no sustituyen la integración con PostgreSQL ni el middleware HTTP.
public class UserServiceTests : IDisposable
{
    private const string Secret = "clave-exclusiva-para-pruebas-de-32-bytes-o-mas";
    private readonly SqliteConnection _connection;
    private readonly UsersDbContext _db;
    private readonly UserRepository _repository;
    private readonly UserService _service;

    public UserServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<UsersDbContext>()
            .UseSqlite(_connection).Options;
        _db = new UsersDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new UserRepository(_db);
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = Secret,
                ["Jwt:Issuer"] = "tests",
                ["Jwt:Audience"] = "tests"
            }).Build();
        _service = new UserService(_repository, new JwtTokenIssuer(config));
    }

    [Fact]
    public void RegisterPersistsHashAndIssuesSignedToken()
    {
        var token = _service.Register(" Ana ", " ANA@Example.com ", "password123");
        _db.ChangeTracker.Clear();
        var user = Assert.Single(_service.GetAll());
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("Ana", user.Name);
        Assert.Equal("ana@example.com", user.Email);
        Assert.NotEqual("password123", user.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("password123", user.PasswordHash));

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            ValidateIssuerSigningKey = true,
            ValidIssuer = "tests",
            ValidAudience = "tests",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        }, out var validatedToken);
        Assert.Equal(user.Id.ToString(), principal.FindFirst("sub")!.Value);
        Assert.Equal(user.Email, principal.FindFirst("email")!.Value);
        Assert.InRange(validatedToken.ValidTo, DateTime.UtcNow.AddMinutes(59), DateTime.UtcNow.AddMinutes(61));
    }

    [Fact]
    public void DuplicateEmailIsRejectedIgnoringCaseAndSpaces()
    {
        _service.Register("Ana", "ana@example.com", "password123");
        Assert.Throws<InvalidOperationException>(() =>
            _service.Register("Otra", " ANA@EXAMPLE.COM ", "password456"));
        Assert.Single(_service.GetAll());
    }

    [Theory]
    [InlineData(" ", "ana@example.com", "password123")]
    [InlineData("Ana", "incorrecto", "password123")]
    [InlineData("Ana", "ana@example.com", "corta")]
    [InlineData("Ana", "ana@example.com", "        ")]
    public void InvalidRegistrationDoesNotPersist(string name, string email, string password)
    {
        Assert.Throws<ArgumentException>(() => _service.Register(name, email, password));
        Assert.Empty(_service.GetAll());
    }

    [Fact]
    public void PasswordLimitCountsUtf8Bytes()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.Register("Ana", "ana@example.com", new string('á', 37)));
        Assert.Empty(_service.GetAll());
    }

    [Fact]
    public void LoginNormalizesEmailAndRejectsIncorrectCredentials()
    {
        _service.Register("Ana", "ana@example.com", "password123");
        Assert.NotEmpty(_service.Authenticate(" ANA@EXAMPLE.COM ", "password123"));
        Assert.Throws<UnauthorizedAccessException>(() => _service.Authenticate("ana@example.com", "incorrecta"));
        Assert.Throws<UnauthorizedAccessException>(() => _service.Authenticate("otro@example.com", "password123"));
        Assert.Throws<UnauthorizedAccessException>(() => _service.Authenticate("ana@example.com", ""));
        Assert.Throws<UnauthorizedAccessException>(() => _service.Authenticate("ana@example.com", new string('a', 73)));
    }

    [Fact]
    public void QueriesAndRepositoryUpdateWork()
    {
        _service.Register("Ana", "ana@example.com", "password123");
        var user = Assert.Single(_service.GetAll());
        user.Name = "Ana María";
        _repository.Save(user);
        _db.ChangeTracker.Clear();
        Assert.Equal("Ana María", _service.GetById(user.Id).Name);
        Assert.True(_service.Exist(user.Id));
        Assert.False(_service.Exist(Guid.NewGuid()));
        Assert.Throws<KeyNotFoundException>(() => _service.GetById(Guid.NewGuid()));
        Assert.Single(_service.GetAll());
    }

    [Fact]
    public void ControllersReturnExpectedStatusesAndPublicUserDto()
    {
        var auth = new AuthController(_service);
        var request = new RegisterRequest("Ana", "ana@example.com", "password123");
        Assert.IsType<OkObjectResult>(auth.Register(request).Result);
        Assert.IsType<ConflictObjectResult>(auth.Register(request).Result);
        Assert.IsType<BadRequestObjectResult>(auth.Register(new RegisterRequest("", "a", "b")).Result);
        Assert.IsType<UnauthorizedObjectResult>(auth.Login(new LoginRequest(request.Email, "incorrecta")).Result);

        var users = new UsersController(_service);
        var user = Assert.Single(_service.GetAll());
        var result = Assert.IsType<OkObjectResult>(users.GetById(user.Id).Result);
        var response = Assert.IsType<UserResponse>(result.Value);
        Assert.Equal(user.Email, response.Email);
        Assert.IsType<NotFoundObjectResult>(users.GetById(Guid.NewGuid()).Result);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
