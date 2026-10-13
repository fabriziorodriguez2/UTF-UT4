using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserManager.Interfaces;

namespace UserManager.Controllers;

[ApiController]
[Route("auth")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly IAutenticacion _auth;

    public AuthController(IAutenticacion auth) => _auth = auth;

    [HttpPost("register")]
    public ActionResult<TokenResponse> Register([FromBody] RegisterRequest req)
    {
        try
        {
            var token = _auth.Register(req.Name, req.Email, req.Password);
            return Ok(new TokenResponse(token));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    public ActionResult<TokenResponse> Login([FromBody] LoginRequest req)
    {
        try
        {
            var token = _auth.Authenticate(req.Email, req.Password);
            return Ok(new TokenResponse(token));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }
}
