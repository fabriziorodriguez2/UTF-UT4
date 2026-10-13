using System.ComponentModel.DataAnnotations;
using System.Text;
using UserManager.Auth;
using UserManager.Domain;
using UserManager.Interfaces;
using UserManager.Repositories;

namespace UserManager.Services;

// Contiene las reglas de negocio; no depende de los controllers.
public class UserService : IUsuarios, IAutenticacion
{
    private readonly UserRepository _repository;
    private readonly JwtTokenIssuer _tokens;

    public UserService(UserRepository repository, JwtTokenIssuer tokens)
    {
        _repository = repository;
        _tokens = tokens;
    }

    // Conservamos el contrato del proyecto: registrar devuelve un JWT.
    public string Register(string name, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre es obligatorio.");

        email = NormalizeEmail(email);
        if (!new EmailAddressAttribute().IsValid(email))
            throw new ArgumentException("El email no es válido.");

        // BCrypt admite hasta 72 bytes, que no siempre equivalen a 72 caracteres.
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8 ||
            Encoding.UTF8.GetByteCount(password) > 72)
            throw new ArgumentException("La contraseña debe tener al menos 8 caracteres y como máximo 72 bytes UTF-8.");

        if (_repository.FindByEmail(email) != null)
            throw new InvalidOperationException("Ya existe un usuario con ese email.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        };

        _repository.Save(user);
        return _tokens.Issue(user);
    }

    public string Authenticate(string email, string password)
    {
        email = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(password) || Encoding.UTF8.GetByteCount(password) > 72)
            throw new UnauthorizedAccessException("Email o contraseña incorrectos.");

        var user = _repository.FindByEmail(email);
        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            throw new UnauthorizedAccessException("Email o contraseña incorrectos.");

        return _tokens.Issue(user);
    }

    public User GetById(Guid id)
    {
        var user = _repository.FindById(id);
        if (user == null)
            throw new KeyNotFoundException("No se encontró el usuario.");

        return user;
    }

    public List<User> GetAll()
    {
        return _repository.GetAll();
    }

    public bool Exist(Guid id)
    {
        return _repository.FindById(id) != null;
    }

    private static string NormalizeEmail(string email)
    {
        return (email ?? string.Empty).Trim().ToLowerInvariant();
    }
}
