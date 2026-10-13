using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManager.Domain;
using TaskManager.Interfaces;
using TaskManager.Services;

namespace TaskManager.Controllers;

/// <summary>
/// Publica ITareas como endpoints REST.
/// </summary>
[ApiController]
[Route("tasks")]
[Authorize]
public class TasksController : ControllerBase
{
    private readonly ITareas _tareas;
    private readonly TaskService _service; // para el endpoint transaccional crear+asignar

    public TasksController(ITareas tareas, TaskService service)
    {
        _tareas = tareas;
        _service = service;
    }

    [HttpPost]
    public ActionResult<TaskResponse> Create([FromBody] CreateTaskRequest req)
    {
        var task = _tareas.CreateTask(req.ProjectId, req.Title);
        return CreatedAtAction(nameof(GetById), new { id = task.Id }, Map(task));
    }

    [HttpPost("create-and-assign")]
    public ActionResult<TaskResponse> CreateAndAssign([FromBody] CreateAndAssignRequest req)
    {
        var task = _service.CreateAndAssign(req.ProjectId, req.Title, req.UserId);
        return CreatedAtAction(nameof(GetById), new { id = task.Id }, Map(task));
    }

    [HttpPost("{id:guid}/assign")]
    public ActionResult<TaskResponse> Assign(Guid id, [FromBody] AssignTaskRequest req)
    {
        var task = _tareas.AssignTask(id, req.UserId);
        return Ok(Map(task));
    }

    [HttpPost("{id:guid}/status")]
    public ActionResult<TaskResponse> ChangeStatus(Guid id, [FromBody] ChangeStatusRequest req)
    {
        var task = _tareas.ChangeStatus(id, req.Status);
        return Ok(Map(task));
    }

    [HttpGet("{id:guid}")]
    public ActionResult<TaskResponse> GetById(Guid id)
    {
        var task = _service.GetById(id);
        return Ok(Map(task));
    }

    [HttpGet]
    public ActionResult<List<TaskResponse>> GetByProject([FromQuery] Guid projectId)
    {
        var tasks = _tareas.GetTasksByProject(projectId);
        return Ok(tasks.Select(Map).ToList());
    }

    private static TaskResponse Map(TaskItem t) =>
        new(t.Id, t.ProjectId, t.Assignee, t.Title, t.Status);
}
