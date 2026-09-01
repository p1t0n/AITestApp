using ExpertToJob.Application.Common;
using FluentValidation;

namespace ExpertToJob.Mcp;

/// <summary>A field-level validation problem, surfaced to the calling agent.</summary>
public record McpFieldError(string Field, string Message);

/// <summary>
/// A structured tool error an external agent can read and self-correct against:
/// a machine <see cref="Code"/>, a human <see cref="Message"/>, and optional per-field detail.
/// </summary>
public record McpToolError(string Code, string Message, IReadOnlyList<McpFieldError> Fields);

/// <summary>
/// Maps the Application layer's domain exceptions to a structured <see cref="McpToolError"/>.
/// Returns null for exceptions that are not a known domain failure (caller should rethrow).
/// </summary>
public static class McpToolErrorMapper
{
    private static readonly IReadOnlyList<McpFieldError> NoFields = Array.Empty<McpFieldError>();

    public static McpToolError? Map(Exception ex) => ex switch
    {
        NotFoundException => new McpToolError("not_found", ex.Message, NoFields),
        ConflictException => new McpToolError("conflict", ex.Message, NoFields),
        ValidationException ve => new McpToolError(
            "validation",
            "One or more fields are invalid.",
            ve.Errors.Select(e => new McpFieldError(e.PropertyName, e.ErrorMessage)).ToList()),
        _ => null,
    };
}
