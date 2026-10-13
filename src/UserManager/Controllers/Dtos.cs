namespace UserManager.Controllers;

// DTOs de entrada/salida de la API REST (lo que viaja por HTTP).
public record RegisterRequest(string Name, string Email, string Password);
public record LoginRequest(string Email, string Password);
public record TokenResponse(string Token);
public record UserResponse(Guid Id, string Name, string Email);
