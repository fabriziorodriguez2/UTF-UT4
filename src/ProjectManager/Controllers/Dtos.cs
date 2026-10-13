namespace ProjectManager.Controllers;

public record CreateProjectRequest(Guid OwnerId, string Name);
public record ProjectResponse(Guid Id, string Name, Guid OwnerId, string Status);
