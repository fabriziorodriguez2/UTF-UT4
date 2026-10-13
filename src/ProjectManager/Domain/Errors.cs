namespace ProjectManager.Domain;

/// <summary>Recurso inexistente → se traduce a HTTP 404.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

/// <summary>Regla de negocio violada (dueño inexistente, nombre vacío, etc.) → HTTP 409.</summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}
