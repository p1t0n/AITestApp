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

        var content = ChunkProjection.Project(employee)
            .Single(c => c.SourceType == SearchChunkSource.Experience).Content;

        content.Should().StartWith("Payments Lead @ BankCo (2019-03–present)");
        content.Should().Contain("Owned the ledger.");
        // Ordered by Order ascending, not insertion order.
        content.IndexOf("Led the payments rewrite.", StringComparison.Ordinal)
            .Should().BeLessThan(content.IndexOf("Cut latency in half.", StringComparison.Ordinal));
    }

    [Fact]
    public void Emits_one_achievement_chunk_per_bullet_keyed_by_achievement_id()
    {
        var exp = Experience("BankCo", "Payments Lead");
        var first = new Achievement { Id = Guid.NewGuid(), Order = 1, Text = "  Led the payments rewrite.  " };
        var second = new Achievement { Id = Guid.NewGuid(), Order = 2, Text = "Cut latency in half." };
        exp.Achievements = [first, second];
        var employee = new Employee { Id = Guid.NewGuid(), Experiences = [exp] };

        var bullets = ChunkProjection.Project(employee)
            .Where(c => c.SourceType == SearchChunkSource.Achievement)
            .ToList();

        bullets.Should().HaveCount(2);
        bullets.Should().OnlyContain(c => c.EmployeeId == employee.Id);
        bullets.Single(c => c.SourceId == first.Id).Content.Should().Be("Led the payments rewrite.");
        bullets.Single(c => c.SourceId == second.Id).Content.Should().Be("Cut latency in half.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Skips_achievement_chunks_for_blank_bullets(string text)
    {
        var exp = Experience("BankCo", "Payments Lead");
        exp.Achievements =
        [
            new Achievement { Id = Guid.NewGuid(), Order = 1, Text = text },
            new Achievement { Id = Guid.NewGuid(), Order = 2, Text = "Real bullet." },
        ];
        var employee = new Employee { Id = Guid.NewGuid(), Experiences = [exp] };

        ChunkProjection.Project(employee)
            .Where(c => c.SourceType == SearchChunkSource.Achievement)
            .Should().ContainSingle().Which.Content.Should().Be("Real bullet.");
    }

    [Fact]
    public void Editing_a_bullet_changes_only_that_achievement_chunks_hash()
    {
        var expId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var editedId = Guid.NewGuid();
        var untouchedId = Guid.NewGuid();

        var before = ProjectBullets(employeeId, expId, (editedId, "Original wording.", 1), (untouchedId, "Stays put.", 2));
        var after = ProjectBullets(employeeId, expId, (editedId, "New wording.", 1), (untouchedId, "Stays put.", 2));

        after.Single(c => c.SourceId == editedId).ContentHash
            .Should().NotBe(before.Single(c => c.SourceId == editedId).ContentHash);
        after.Single(c => c.SourceId == untouchedId).ContentHash
            .Should().Be(before.Single(c => c.SourceId == untouchedId).ContentHash);
    }

    [Fact]
    public void Deleting_a_bullet_removes_its_desired_chunk()
    {
        var expId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var keptId = Guid.NewGuid();

        var after = ProjectBullets(employeeId, expId, (keptId, "Kept.", 1));

        var before = ProjectBullets(employeeId, expId, (deletedId, "Doomed.", 1), (keptId, "Kept.", 2));
        before.Should().Contain(c => c.SourceId == deletedId);
        after.Should().ContainSingle().Which.SourceId.Should().Be(keptId);
    }

    [Fact]
    public void Achievement_chunks_leave_the_experience_chunk_content_untouched()
    {
        var exp = Experience("BankCo", "Payments Lead", summary: "Owned the ledger.");
        exp.StartDate = new DateOnly(2019, 3, 1);
        exp.Achievements = [new Achievement { Id = Guid.NewGuid(), Order = 1, Text = "Led the payments rewrite." }];
        var employee = new Employee { Id = Guid.NewGuid(), Experiences = [exp] };

        var experienceChunk = ChunkProjection.Project(employee)
            .Single(c => c.SourceType == SearchChunkSource.Experience);

        // The exact pre-P1T-63 rendering: bullets stay rolled into the experience narrative.
        experienceChunk.Content.Should().Be(
            "Payments Lead @ BankCo (2019-03–present)\nOwned the ledger.\n- Led the payments rewrite.");
    }

    private static List<DesiredChunk> ProjectBullets(
        Guid employeeId, Guid expId, params (Guid Id, string Text, int Order)[] achievements)
    {
        var exp = Experience("Co", "Role");
        exp.Id = expId;
        exp.Achievements = achievements
            .Select(a => new Achievement { Id = a.Id, Text = a.Text, Order = a.Order })
            .ToList();
        var employee = new Employee { Id = employeeId, Experiences = [exp] };
        return ChunkProjection.Project(employee)
            .Where(c => c.SourceType == SearchChunkSource.Achievement)
            .ToList();
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
