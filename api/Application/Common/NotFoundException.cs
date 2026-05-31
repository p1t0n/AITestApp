namespace EmployeeManager.Application.Common;

/// <summary>Thrown when a requested entity does not exist. Mapped to HTTP 404 in the Web layer.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string entity, object key)
        : base($"{entity} '{key}' was not found.") { }
}
