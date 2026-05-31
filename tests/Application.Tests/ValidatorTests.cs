using EmployeeManager.Application.Availability;
using EmployeeManager.Application.Employees;
using FluentAssertions;
using Xunit;

namespace EmployeeManager.Application.Tests;

public class ValidatorTests
{
    [Fact]
    public void SaveEmployee_requires_name_and_valid_email()
    {
        var result = new SaveEmployeeValidator().Validate(
            new SaveEmployeeDto("", "X", "T", "not-an-email", null, null, null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.PropertyName).Should().Contain(new[] { "FirstName", "Email" });
    }

    [Fact]
    public void SaveEmployee_passes_for_valid_input()
    {
        var result = new SaveEmployeeValidator().Validate(
            new SaveEmployeeDto("Ada", "Lovelace", "Engineer", "ada@example.com", null, "London", null, null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Availability_capacity_must_be_0_to_100()
    {
        var v = new SaveAvailabilityEntryValidator();

        v.Validate(new SaveAvailabilityEntryDto(new DateOnly(2027, 1, 1), 150)).IsValid.Should().BeFalse();
        v.Validate(new SaveAvailabilityEntryDto(new DateOnly(2027, 1, 1), -1)).IsValid.Should().BeFalse();
        v.Validate(new SaveAvailabilityEntryDto(new DateOnly(2027, 1, 1), 75)).IsValid.Should().BeTrue();
    }
}
