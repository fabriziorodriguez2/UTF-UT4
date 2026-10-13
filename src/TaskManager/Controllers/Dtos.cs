namespace TaskManager.Controllers;

public record CreateTaskRequest(Guid ProjectId, string Title);
public record CreateAndAssignRequest(Guid ProjectId, string Title, Guid UserId);
public record AssignTaskRequest(Guid UserId);
public record ChangeStatusRequest(string Status);
public record TaskResponse(Guid Id, Guid ProjectId, Guid Assignee, string Title, string Status);
