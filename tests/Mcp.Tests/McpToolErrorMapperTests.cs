using CvManager.Application.Common;
using CvManager.Mcp;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Xunit;

namespace CvManager.Mcp.Tests;

public class McpToolErrorMapperTests
{
    [Fact]
    public void Maps_NotFoundException_to_not_found_code()
    {
        var error = McpToolErrorMapper.Map(new NotFoundException("Employee", Guid.NewGuid()));

        error.Should().NotBeNull();
        error!.Code.Should().Be("not_found");
        error.Message.Should().Contain("Employee");
    }

    [Fact]
    public void Maps_ConflictException_to_conflict_code()
    {
        var error = McpToolErrorMapper.Map(new ConflictException("Employee already has this skill."));

        error.Should().NotBeNull();
        error!.Code.Should().Be("conflict");
        error.Message.Should().Be("Employee already has this skill.");
    }

    [Fact]
    public void Maps_ValidationException_to_validation_code_with_field_detail()
    {
        var failures = new[]
        {
            new ValidationFailure("FirstName", "FirstName must not be empty."),
            new ValidationFailure("Email", "Email is invalid."),
        };

        var error = McpToolErrorMapper.Map(new ValidationException(failures));

        error.Should().NotBeNull();
        error!.Code.Should().Be("validation");
        error.Fields.Should().BeEquivalentTo(new[]
        {
            new McpFieldError("FirstName", "FirstName must not be empty."),
            new McpFieldError("Email", "Email is invalid."),
        });
    }

    [Fact]
    public void Returns_null_for_unknown_exception()
    {
        McpToolErrorMapper.Map(new InvalidOperationException("boom")).Should().BeNull();
    }
}
