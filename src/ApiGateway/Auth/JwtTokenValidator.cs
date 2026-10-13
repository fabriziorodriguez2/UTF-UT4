using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ApiGateway.Auth;

/// <summary>
/// Valida el JWT usando el secreto/issuer/audience compartidos (config Jwt:*).
/// </summary>
public class JwtTokenValidator : ITokenValidator
{
    private readonly TokenValidationParameters _parameters;

    public JwtTokenValidator(IConfiguration config)
    {
        var secret = config["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Jwt:Secret debe tener al menos 32 bytes UTF-8.");
        if (string.IsNullOrWhiteSpace(config["Jwt:Issuer"]) || string.IsNullOrWhiteSpace(config["Jwt:Audience"]))
            throw new InvalidOperationException("Se deben configurar Jwt:Issuer y Jwt:Audience.");

        _parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = true,
            ValidIssuer = config["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = config["Jwt:Audience"],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ClockSkew = TimeSpan.Zero
        };
    }

    public bool IsValid(string? bearerToken)
    {
        if (!AuthenticationHeaderValue.TryParse(bearerToken, out var header) ||
            !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
            return false;

        try
        {
            new JwtSecurityTokenHandler().ValidateToken(header.Parameter, _parameters, out _);
            return true;
        }
        catch (SecurityTokenException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
