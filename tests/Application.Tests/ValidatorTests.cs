using ExpertToJob.Application.Availability;
using ExpertToJob.Application.Experts;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Application.Tests;

public class ValidatorTests
{
    [Fact]
    public void SaveExpert_requires_name_and_valid_email()
    {
        var result = new SaveExpertValidator().Validate(
            new SaveExpertDto("", "X", "T", "not-an-email", null, null, null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.PropertyName).Should().Contain(new[] { "FirstName", "Email" });
    }

    [Fact]
    public void SaveExpert_passes_for_valid_input()
    {
        var result = new SaveExpertValidator().Validate(
            new SaveExpertDto("Ada", "Lovelace", "Engineer", "ada@example.com", null, "London", null, null));

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
