using FluentAssertions;

namespace EmployeeManager.Mcp.Tests.Eval;

/// <summary>
/// Unit tests for the pure fixture-to-entity mapping: what the eval seeds must be exactly what the
/// corpus JSON says, chunk-projectable the same way production employees are.
/// </summary>
public class EvalCorpusSeederTests
{
    private static readonly EvalEmployee Fixture = new(
        Key: "ada-l",
        FirstName: "Ada",
        LastName: "Lovelace",
        Title: "Analyst",
        Location: "London",
        Summary: "Wrote the first program.",
        Experiences:
        [
            new EvalExperience(
                Company: "Analytical Engines Ltd",
                Title: "Programmer",
                StartMonth: "1842-05",
                EndMonth: "1843-09",
                Summary: "Translated and annotated.",
                Achievements: ["Note G.", "First published algorithm."]),
            new EvalExperience(
                Company: "Freelance",
                Title: "Mathematician",
                StartMonth: "1840-01",
                EndMonth: null,
                Summary: "Ongoing studies.",
                Achievements: []),
        ]);

    [Fact]
    public void Maps_identity_fields_and_narrative_text()
    {
        var employee = EvalCorpusSeeder.ToEmployee(Fixture);

        employee.Id.Should().NotBeEmpty();
        employee.FirstName.Should().Be("Ada");
        employee.LastName.Should().Be("Lovelace");
        employee.Title.Should().Be("Analyst");
        employee.Location.Should().Be("London");
        employee.Summary.Should().Be("Wrote the first program.");
        employee.Email.Should().Be("ada-l@eval.example.com");
    }

    [Fact]
    public void Maps_experiences_with_month_precision_dates()
    {
        var employee = EvalCorpusSeeder.ToEmployee(Fixture);

        employee.Experiences.Should().HaveCount(2);
        var first = employee.Experiences.First();
        first.Company.Should().Be("Analytical Engines Ltd");
        first.Title.Should().Be("Programmer");
        first.StartDate.Should().Be(new DateOnly(1842, 5, 1));
        first.EndDate.Should().Be(new DateOnly(1843, 9, 1));
        first.Summary.Should().Be("Translated and annotated.");
    }

    [Fact]
    public void Null_end_month_means_a_current_role()
        => EvalCorpusSeeder.ToEmployee(Fixture)
            .Experiences.Last().EndDate.Should().BeNull();

    [Fact]
    public void Achievements_keep_their_authored_order()
        => EvalCorpusSeeder.ToEmployee(Fixture)
            .Experiences.First().Achievements.OrderBy(a => a.Order)
            .Select(a => a.Text)
            .Should().Equal("Note G.", "First published algorithm.");
}
