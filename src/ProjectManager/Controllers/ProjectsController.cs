using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectManager.Domain;
using ProjectManager.Interfaces;

namespace ProjectManager.Controllers;

/// <summary>
/// Publica IProyectos como endpoints REST.
/// Las excepciones de dominio las traduce ExceptionHandlingMiddleware (404/409).
/// </summary>
[ApiController]
[Route("projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly IProyectos _proyectos;

    public ProjectsController(IProyectos proyectos) => _proyectos = proyectos;

    [HttpPost]
    public ActionResult<ProjectResponse> Create([FromBody] CreateProjectRequest req)
    {
        var project = _proyectos.CreateProject(req.OwnerId, req.Name);
        return CreatedAtAction(nameof(GetById), new { id = project.Id }, Map(project));
    }

    [HttpGet("{id:guid}")]
    public ActionResult<ProjectResponse> GetById(Guid id)
    {
        return Ok(Map(_proyectos.GetById(id)));
    }

    [HttpGet]
    public ActionResult<List<ProjectResponse>> GetByUser([FromQuery] Guid userId)
    {
        var projects = _proyectos.GetProjectsByUser(userId);
        return Ok(projects.Select(Map).ToList());
    }

    [HttpPost("{id:guid}/close")]
    public IActionResult Close(Guid id)
    {
        _proyectos.CloseProject(id);
        return NoContent();
    }

    /// <summary>Consumido por TaskManager antes de crear/mover tareas.</summary>
    [HttpGet("{id:guid}/is-open")]
    public ActionResult<bool> IsOpen(Guid id)
    {
        _proyectos.GetById(id);   // proyecto inexistente → 404
        return Ok(_proyectos.IsOpen(id));
    }

    private static ProjectResponse Map(Project p) =>
        new(p.Id, p.Name, p.OwnerId, p.Status);
}
