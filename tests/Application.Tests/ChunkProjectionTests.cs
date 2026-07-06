using EmployeeManager.Application.Search;
using EmployeeManager.Domain.Entities;
using EmployeeManager.Domain.Enums;
using FluentAssertions;

namespace EmployeeManager.Application.Tests;

public class ChunkProjectionTests
{
    [Fact]
    public void Produces_a_summary_chunk_and_one_chunk_per_experience()
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            Summary = "Seasoned backend engineer.",
            Experiences =
            [
                Experience("BankCo", "Payments Lead"),
                Experience("ShopCo", "Senior Engineer"),
            ],
        };

        var chunks = ChunkProjection.Project(employee);

        chunks.Should().HaveCount(3);
        chunks.Count(c => c.SourceType == SearchChunkSource.Summary).Should().Be(1);
        chunks.Count(c => c.SourceType == SearchChunkSource.Experience).Should().Be(2);
        chunks.Should().OnlyContain(c => c.EmployeeId == employee.Id);
    }

    [Fact]
    public void Summary_chunk_is_keyed_by_employee_id()
    {
        var employee = new Employee { Id = Guid.NewGuid(), Summary = "Bio." };

        var summary = ChunkProjection.Project(employee).Single();

        summary.SourceType.Should().Be(SearchChunkSource.Summary);
        summary.SourceId.Should().Be(employee.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Skips_the_summary_chunk_when_summary_is_blank(string? summary)
    {
        var employee = new Employee { Id = Guid.NewGuid(), Summary = summary, Experiences = [Experience("X", "Y")] };

        var chunks = ChunkProjection.Project(employee);

        chunks.Should().ContainSingle().Which.SourceType.Should().Be(SearchChunkSource.Experience);
    }

    [Fact]
    public void Experience_chunk_contains_header_summary_and_ordered_achievements()
    {
        var exp = Experience("BankCo", "Payments Lead", summary: "Owned the ledger.");
        exp.StartDate = new DateOnly(2019, 3, 1);
        exp.EndDate = null; // current role
        exp.Achievements =
        [
            new Achievement { Order = 2, Text = "Cut latency in half." },
            new Achievement { Order = 1, Text = "Led the payments rewrite." },
        ];
        var employee = new Employee { Id = Guid.NewGuid(), Experiences = [exp] };

        var content = ChunkProjection.Project(employee).Single().Content;

        content.Should().StartWith("Payments Lead @ BankCo (2019-03–present)");
        content.Should().Contain("Owned the ledger.");
        // Ordered by Order ascending, not insertion order.
        content.IndexOf("Led the payments rewrite.", StringComparison.Ordinal)
            .Should().BeLessThan(content.IndexOf("Cut latency in half.", StringComparison.Ordinal));
    }

    [Fact]
    public void Hash_is_stable_for_identical_content_and_changes_with_content()
    {
        var a = ChunkProjection.Hash("same text");
        var b = ChunkProjection.Hash("same text");
        var c = ChunkProjection.Hash("different text");

        a.Should().Be(b);
        a.Should().NotBe(c);
        a.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Reordering_achievements_changes_the_experience_hash()
    {
        var employee = Guid.NewGuid();
        var expId = Guid.NewGuid();

        var before = BuildExperienceChunk(employee, expId, ("A", 1), ("B", 2));
        var after = BuildExperienceChunk(employee, expId, ("A", 2), ("B", 1));

        after.ContentHash.Should().NotBe(before.ContentHash);
    }

    private static DesiredChunk BuildExperienceChunk(Guid employeeId, Guid expId, params (string Text, int Order)[] achievements)
    {
        var exp = Experience("Co", "Role");
        exp.Id = expId;
        exp.Achievements = achievements.Select(a => new Achievement { Text = a.Text, Order = a.Order }).ToList();
        var employee = new Employee { Id = employeeId, Experiences = [exp] };
        return ChunkProjection.Project(employee).Single(c => c.SourceType == SearchChunkSource.Experience);
    }

    private static Experience Experience(string company, string title, string? summary = null) => new()
    {
        Id = Guid.NewGuid(),
        Company = company,
        Title = title,
        Summary = summary,
        StartDate = new DateOnly(2020, 1, 1),
    };
}
