using System.Text.Json;
using ExpertToJob.Agents.Agents;
using ExpertToJob.Application.Cv;
using ExpertToJob.Application.Experts;
using ExpertToJob.Application.Search;
using ExpertToJob.Application.Skills;
using ExpertToJob.Domain.Enums;
using FluentAssertions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Which Experts a turn touched, read off the tool results in code (EXP-36,
/// <c>manuals/adr-roster-qa-conversation-history.md</c> §3). This set is what erasure later uses
/// to find the turns that must be scrubbed, so a miss here is a privacy failure that surfaces
/// months later — and the model's own prose is never an input to it.
///
/// <para>The canned results are the real DTOs the MCP tools return, serialized the way the server
/// serializes them, so a DTO that renames a field fails here rather than silently narrowing the
/// set.</para>
/// </summary>
public class TouchedExpertScannerTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly Guid Ada = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid Grace = Guid.Parse("60000000-0000-0000-0000-000000000060");

    /// <summary>The tool result as the wire carries it: JSON, not a POCO.</summary>
    private static object Wire(object payload) =>
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload, Web), Web);

    private static IReadOnlyCollection<Guid> Scan(object? result) =>
        TouchedExpertScanner.Scan(result, arguments: null);

    // ---- one canned result per Roster Q&A read tool --------------------------------------------

    [Fact]
    public void expert_list_rows_are_experts_by_their_own_id()
    {
        var rows = new[]
        {
            new ExpertSummaryDto(Ada, "Ada", "Lovelace", "Engineer", "London", "ada@example.com", 50, ExpertStatus.Active),
            new ExpertSummaryDto(Grace, "Grace", "Hopper", "Admiral", "Arlington", "grace@example.com", 0, ExpertStatus.Active),
        };

        Scan(Wire(rows)).Should().BeEquivalentTo([Ada, Grace]);
    }

    [Fact]
    public void roster_semantic_search_hits_are_experts_by_expertId()
    {
        var result = new SemanticSearchResult(
        [
            new SemanticSearchHit(Ada, "Ada Lovelace", "Engineer", 0.82, ["led a payments migration"]),
        ]);

        Scan(Wire(result)).Should().BeEquivalentTo([Ada]);
    }

    [Fact]
    public void roster_shortlist_search_candidates_are_experts_by_expertId()
    {
        var result = new ShortlistSearchResult(
        [
            new ShortlistCandidate(Grace, "Grace Hopper", "Admiral", 0.9, 2, 3,
                [new ShortlistRequirementEvidence("COBOL", true, "wrote the compiler", 0.9)]),
        ]);

        Scan(Wire(result)).Should().BeEquivalentTo([Grace]);
    }

    [Fact]
    public void roster_digest_pages_are_experts_by_expertId()
    {
        var page = new ExpertDigestPage(1, 50, 1, [new ExpertDigest(Ada, "Ada Lovelace", "Engineer", "…")]);

        Scan(Wire(page)).Should().BeEquivalentTo([Ada]);
    }

    /// <summary>
    /// The one read tool whose result names nobody: <c>cv_get</c> returns a CV, and a CV has a full
    /// name and no id. The id it was called with is therefore the only in-code evidence that this
    /// person's prose went into the prompt — and it is the turn most worth scrubbing.
    /// </summary>
    [Fact]
    public void cv_get_is_touched_through_the_expertId_it_was_called_with()
    {
        var cv = new CvDto(
            "Ada Lovelace", "Engineer", "ada@example.com", null, "London", "Summary", null,
            new CvAvailabilityDto(50, []), [], [],
            [new CvExperienceDto(Guid.NewGuid(), "Analytical Engines", "Engineer", "London", "1842–1843",
                null, [new CvAchievementDto(Guid.NewGuid(), "Wrote the first algorithm.")], [])],
            [], []);

        var touched = TouchedExpertScanner.Scan(
            Wire(cv), new Dictionary<string, object?> { ["expertId"] = Ada });

        touched.Should().BeEquivalentTo([Ada]);
    }

    /// <summary>The CV's own ids — an experience row, an achievement bullet — are not people, and
    /// a turn scrubbed because a bullet id happened to be a GUID would be a bug nobody could
    /// explain.</summary>
    [Fact]
    public void A_cv_contributes_none_of_its_own_child_ids()
    {
        var cv = new CvDto(
            "Ada Lovelace", "Engineer", "ada@example.com", null, "London", null, null,
            new CvAvailabilityDto(50, []), [], [],
            [new CvExperienceDto(Guid.NewGuid(), "Analytical Engines", "Engineer", "London", "1842–1843",
                null, [new CvAchievementDto(Guid.NewGuid(), "Wrote the first algorithm.")], [])],
            [], []);

        Scan(Wire(cv)).Should().BeEmpty();
    }

    [Fact]
    public void skill_list_ids_are_skills_and_are_never_collected()
    {
        var skills = new[]
        {
            new SkillDto(Guid.NewGuid(), "React", Guid.NewGuid(), "Frontend", 1),
            new SkillDto(Guid.NewGuid(), "COBOL", Guid.NewGuid(), "Languages", 2),
        };

        Scan(Wire(skills)).Should().BeEmpty();
    }

    [Fact]
    public void An_experts_skill_rows_are_not_mistaken_for_experts()
    {
        var detail = new
        {
            id = Ada,
            firstName = "Ada",
            lastName = "Lovelace",
            email = "ada@example.com",
            skills = new[]
            {
                new ExpertSkillDto(Guid.NewGuid(), Guid.NewGuid(), "React", "Frontend", SkillLevel.Expert, 5m),
            },
        };

        Scan(Wire(detail)).Should().BeEquivalentTo([Ada], "the person, and neither the row id nor the skill id");
    }

    // ---- the shapes a tool result actually arrives in -------------------------------------------

    /// <summary>
    /// How the Agent Framework hands back an MCP tool result: one text content block whose text is
    /// the payload JSON. Missing this shape was a production bug in the shortlist flow
    /// (<c>ToolResultPayload</c>), and it would be a silent one here — every set empty.
    /// </summary>
    [Fact]
    public void A_text_content_block_carrying_the_payload_json_is_walked_into()
    {
        var payload = JsonSerializer.Serialize(
            new[] { new SemanticSearchHit(Ada, "Ada Lovelace", "Engineer", 0.8, []) }, Web);

        Scan(Wire(new { type = "text", text = payload })).Should().BeEquivalentTo([Ada]);
    }

    [Fact]
    public void An_mcp_envelope_with_content_blocks_is_walked_into()
    {
        var payload = JsonSerializer.Serialize(
            new[] { new ExpertSummaryDto(Grace, "Grace", "Hopper", "Admiral", null, "g@example.com", 100, ExpertStatus.Active) },
            Web);

        var envelope = new { isError = false, content = new[] { new { type = "text", text = payload } } };

        Scan(Wire(envelope)).Should().BeEquivalentTo([Grace]);
    }

    [Fact]
    public void A_tool_error_envelope_names_nobody()
    {
        var error = new { isError = true, content = new[] { new { type = "text", text = """{"code":"not_found"}""" } } };

        Scan(Wire(error)).Should().BeEmpty();
    }

    [Fact]
    public void Prose_that_merely_quotes_a_guid_is_never_parsed_for_ids()
    {
        // The model's own text is not an input to this. A GUID inside an answer string is text.
        Scan(Wire(new { answer = $"Ada Lovelace ({Ada}) knows React." })).Should().BeEmpty();
    }

    [Fact]
    public void The_same_expert_across_two_results_is_reported_once()
    {
        var first = Wire(new[] { new SemanticSearchHit(Ada, "Ada", "Engineer", 0.9, []) });
        var second = Wire(new[]
        {
            new ExpertSummaryDto(Ada, "Ada", "Lovelace", "Engineer", null, "ada@example.com", 50, ExpertStatus.Active),
        });

        var both = Scan(first).Concat(Scan(second)).Distinct().ToList();
        both.Should().ContainSingle().Which.Should().Be(Ada);
    }

    [Fact]
    public void A_null_result_and_an_unparseable_one_are_both_empty_rather_than_a_throw()
    {
        Scan(null).Should().BeEmpty();
        Scan("not json at all").Should().BeEmpty();
    }
}
