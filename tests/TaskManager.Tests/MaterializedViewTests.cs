using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaskManager.Contracts;
using TaskManager.Controllers;
using TaskManager.Domain;
using TaskManager.Persistence;
using TaskManager.Repositories;
using TaskManager.Services;
using Xunit;

namespace TaskManager.Tests;

// SQLite verifica persistencia y transacciones sin Docker. No sustituye la
// prueba del esquema PostgreSQL ni la integración HTTP entre contenedores.
public class MaterializedViewTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TasksDbContext _db;
    private readonly UsersStub _users = new();
    private readonly ProjectsStub _projects = new();
    private readonly QueryCounter _queries = new();
    private readonly TaskService _service;
    private readonly Guid _projectId = Guid.NewGuid();

    public MaterializedViewTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new TasksDbContext(new DbContextOptionsBuilder<TasksDbContext>()
            .UseSqlite(_connection).AddInterceptors(_queries).Options);
        _db.Database.EnsureCreated();
        _service = new TaskService(new TaskRepository(_db), _projects, _users, _db);
    }

    [Fact]
    public void AssignmentPersistsNameWithOneUserLookup()
    {
        var task = _service.CreateTask(_projectId, "Informe");
        var user = AddUser("Ana");

        _service.AssignTask(task.Id, user.Id);

        Assert.Equal(1, _users.Lookups);
        Assert.Equal(user.Id, Assert.Single(_users.RequestedIds));
        _db.ChangeTracker.Clear();
        var saved = _service.GetById(task.Id);
        Assert.Equal(user.Id, saved.Assignee);
        Assert.Equal("Ana", saved.AssigneeName);
    }

    [Fact]
    public void ReassignmentReplacesBothIdAndName()
    {
        var task = _service.CreateTask(_projectId, "Informe");
        var ana = AddUser("Ana");
        var bruno = AddUser("Bruno");
        _service.AssignTask(task.Id, ana.Id);

        _service.AssignTask(task.Id, bruno.Id);

        Assert.Equal(2, _users.Lookups);
        _db.ChangeTracker.Clear();
        var saved = _service.GetById(task.Id);
        Assert.Equal(bruno.Id, saved.Assignee);
        Assert.Equal("Bruno", saved.AssigneeName);
    }

    [Fact]
    public void CreateAndAssignPersistsNameWithOneUserLookup()
    {
        var user = AddUser("Ana");

        var task = _service.CreateAndAssign(_projectId, "Informe", user.Id);

        Assert.Equal(1, _users.Lookups);
        _db.ChangeTracker.Clear();
        var saved = Assert.Single(_service.GetTasksByProject(_projectId));
        Assert.Equal(task.Id, saved.Id);
        Assert.Equal(user.Id, saved.Assignee);
        Assert.Equal("Ana", saved.AssigneeName);
    }

    [Fact]
    public void BoardReadsOneLocalQueryWhenOtherServicesAreUnavailable()
    {
        var user = AddUser("Ana");
        var assigned = _service.CreateAndAssign(_projectId, "Asignada", user.Id);
        var unassigned = _service.CreateTask(_projectId, "Sin asignar");
        _service.CreateTask(Guid.NewGuid(), "Otro proyecto");
        _db.ChangeTracker.Clear();
        _users.Unavailable = true;
        _projects.Unavailable = true;
        _users.Lookups = 0;
        _queries.Reads = 0;

        var controller = new TasksController(_service, _service);
        var result = Assert.IsType<OkObjectResult>(controller.GetByProject(_projectId).Result);
        var board = Assert.IsType<List<TaskResponse>>(result.Value);

        Assert.Equal(1, _queries.Reads);
        Assert.Equal(0, _users.Lookups);
        Assert.Equal(2, board.Count);
        var card = Assert.Single(board, t => t.Id == assigned.Id);
        Assert.Equal(user.Id, card.Assignee);
        Assert.Equal("Ana", card.AssigneeName);
        var emptyCard = Assert.Single(board, t => t.Id == unassigned.Id);
        Assert.Equal(Guid.Empty, emptyCard.Assignee);
        Assert.Null(emptyCard.AssigneeName);
    }

    [Fact]
    public void UserRenameIsReflectedOnNextAssignment()
    {
        var user = AddUser("Ana");
        var task = _service.CreateAndAssign(_projectId, "Informe", user.Id);
        _users.Users[user.Id] = user with { Name = "Ana María" };
        _db.ChangeTracker.Clear();

        Assert.Equal("Ana", Assert.Single(_service.GetTasksByProject(_projectId)).AssigneeName);
        Assert.Equal(1, _users.Lookups);
        _service.AssignTask(task.Id, user.Id);
        _db.ChangeTracker.Clear();
        Assert.Equal("Ana María", _service.GetById(task.Id).AssigneeName);
        Assert.Equal(2, _users.Lookups);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedAssignmentPreservesPreviousIdAndName(bool unavailable)
    {
        var user = AddUser("Ana");
        var task = _service.CreateAndAssign(_projectId, "Informe", user.Id);
        _users.Unavailable = unavailable;

        if (unavailable)
            Assert.Throws<HttpRequestException>(() => _service.AssignTask(task.Id, Guid.NewGuid()));
        else
            Assert.Throws<BusinessRuleException>(() => _service.AssignTask(task.Id, Guid.NewGuid()));

        _db.ChangeTracker.Clear();
        var saved = _service.GetById(task.Id);
        Assert.Equal(user.Id, saved.Assignee);
        Assert.Equal("Ana", saved.AssigneeName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCreateAndAssignDoesNotLeaveTask(bool unavailable)
    {
        _users.Unavailable = unavailable;

        if (unavailable)
            Assert.Throws<HttpRequestException>(() => _service.CreateAndAssign(_projectId, "Informe", Guid.NewGuid()));
        else
            Assert.Throws<BusinessRuleException>(() => _service.CreateAndAssign(_projectId, "Informe", Guid.NewGuid()));

        Assert.Equal(1, _users.Lookups);
        _db.ChangeTracker.Clear();
        Assert.Empty(_service.GetTasksByProject(_projectId));
    }

    [Fact]
    public void MissingTaskDoesNotCallUserManager()
    {
        Assert.Throws<NotFoundException>(() => _service.AssignTask(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(0, _users.Lookups);
    }

    [Fact]
    public void StatusChangeAndDetailKeepMaterializedNameWithoutUserLookup()
    {
        var user = AddUser("Ana");
        var task = _service.CreateAndAssign(_projectId, "Informe", user.Id);
        _users.Unavailable = true;
        _users.Lookups = 0;
        var controller = new TasksController(_service, _service);

        var changed = Assert.IsType<OkObjectResult>(controller.ChangeStatus(task.Id,
            new ChangeStatusRequest(TaskStatuses.Done)).Result);
        Assert.Equal("Ana", Assert.IsType<TaskResponse>(changed.Value).AssigneeName);
        _db.ChangeTracker.Clear();
        var detail = Assert.IsType<OkObjectResult>(controller.GetById(task.Id).Result);
        var response = Assert.IsType<TaskResponse>(detail.Value);
        Assert.Equal("Ana", response.AssigneeName);
        Assert.Equal(TaskStatuses.Done, response.Status);
        Assert.Equal(0, _users.Lookups);
    }

    private UserDto AddUser(string name)
    {
        var user = new UserDto(Guid.NewGuid(), name, "usuario@example.com");
        _users.Users.Add(user.Id, user);
        return user;
    }

    private sealed class UsersStub : IUsuarios
    {
        public Dictionary<Guid, UserDto> Users { get; } = new();
        public List<Guid> RequestedIds { get; } = new();
        public int Lookups { get; set; }
        public bool Unavailable { get; set; }

        public bool Exist(Guid id) => throw new InvalidOperationException("No debe usarse /exists para materializar.");

        public UserDto? GetById(Guid id)
        {
            Lookups++;
            RequestedIds.Add(id);
            if (Unavailable) throw new HttpRequestException("UserManager no disponible.");
            return Users.GetValueOrDefault(id);
        }
    }

    private sealed class ProjectsStub : IProyectos
    {
        public bool Unavailable { get; set; }
        public bool IsOpen(Guid projectId) => Unavailable
            ? throw new HttpRequestException("ProjectManager no disponible.") : true;
        public ProjectDto? GetById(Guid projectId) => throw new InvalidOperationException();
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Reads { get; set; }
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Reads++;
            return result;
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
