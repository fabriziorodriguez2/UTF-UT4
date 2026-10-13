namespace TaskManager.Domain;

/// <summary>Recurso inexistente → se traduce a HTTP 404.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

/// <summary>Regla de negocio violada (proyecto cerrado, estado inválido, etc.) → HTTP 409/400.</summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}
