using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserManager.Interfaces;

namespace UserManager.Controllers;

[ApiController]
[Route("users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUsuarios _usuarios;

    public UsersController(IUsuarios usuarios) => _usuarios = usuarios;

    [HttpGet("{id:guid}")]
    public ActionResult<UserResponse> GetById(Guid id)
    {
        try
        {
            var user = _usuarios.GetById(id);
            return Ok(new UserResponse(user.Id, user.Name, user.Email));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet]
    public ActionResult<List<UserResponse>> GetAll()
    {
        var response = new List<UserResponse>();
        foreach (var user in _usuarios.GetAll())
        {
            // La respuesta HTTP nunca incluye el hash de la contraseña.
            response.Add(new UserResponse(user.Id, user.Name, user.Email));
        }
        return Ok(response);
    }

    [HttpGet("{id:guid}/exists")]
    public ActionResult<bool> Exists(Guid id)
    {
        return Ok(_usuarios.Exist(id));
    }
}
