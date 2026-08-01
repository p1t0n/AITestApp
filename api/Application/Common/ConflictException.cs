namespace CvManager.Application.Common;

/// <summary>Thrown when an operation conflicts with current state. Mapped to HTTP 409 in the Web layer.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
