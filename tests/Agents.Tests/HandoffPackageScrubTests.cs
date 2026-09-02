using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Handoff;
using ExpertToJob.Agents.Staffing;
using ExpertToJob.Application.Compliance;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The typed half of the package scrub (P1T-186). The scrub itself lives in the Application layer
/// and walks JSON, because the Web host that serves erasure cannot reference these record types —
/// so the guarantee that matters is proven from this side instead: a <b>real</b> serialized
/// document goes through the scrub, comes back through the <b>real</b> <c>TryDeserialize</c>, and
/// has to still be a document.
///
/// <para>Without this pair the scrub would be exactly the blind jsonb surgery the design rejected:
/// path-based edits nobody ever fed a genuine document.</para>
/// </summary>
public class HandoffPackageScrubTests
{
    private static readonly Guid Erased = Guid.NewGuid();
    private static readonly Guid Bystander = Guid.NewGuid();
    // Fixed, not fresh per call: two builds of the same document are compared field by field.
    private static readonly Guid Requester = Guid.NewGuid();

    [Fact]
    public void The_document_still_deserializes_after_the_scrub()
    {
        var scrubbed = ScrubbedDocument();

        scrubbed.Should().NotBeNull(
            "the proposal is a decision record and the approver must still be able to read it");
    }

    [Fact]
    public void Everything_the_report_said_about_the_erased_person_is_gone()
    {
        var scrubbed = ScrubbedDocument()!;
        var candidate = scrubbed.Report.Candidates.Single(c => c.ExpertId == Erased);

        candidate.Name.Should().BeNull();
        candidate.Title.Should().BeNull();
        candidate.Rationale.Should().BeNull();
        candidate.Match.Answer.Should().BeNull();
        candidate.Shortlist.Requirements.Should().OnlyContain(r => r.Snippet == null,
            "the evidence snippets are the person's own CV text, quoted verbatim");
        scrubbed.Report.Recommendation!.Narrative.Should().BeNull();

        scrubbed.Serialize().Should().NotContain("Zarquon");
    }

    /// <summary>
    /// The other direction, and the one a careless path-walk fails: nobody else in the same
    /// document loses anything. A proposal names several candidates and erasing one of them must
    /// not hollow out the rest of somebody's decision record.
    /// </summary>
    [Fact]
    public void Nobody_else_in_the_document_is_touched()
    {
        var scrubbed = ScrubbedDocument()!;
        var bystander = scrubbed.Report.Candidates.Single(c => c.ExpertId == Bystander);

        bystander.Name.Should().Be("Wren Ashgrove");
        bystander.Title.Should().Be("Platform Engineer");
        bystander.Rationale.Should().Be("Wren has shipped this twice.");
        bystander.Match.Answer.Should().Be("Wren scores 71.");
        bystander.Shortlist.Requirements.Single().Snippet.Should().Be("Wren ran the platform.");
    }

    /// <summary>
    /// The structure is the guarantee (see <c>manuals/handoff-package.md</c>): everything that is
    /// not one of the six personal fields survives byte for byte, because the approver decides from
    /// the package alone and a scrub that quietly dropped the provenance would leave them deciding
    /// from less than they think.
    /// </summary>
    [Fact]
    public void The_rest_of_the_document_survives_intact()
    {
        var original = Document();
        var scrubbed = ScrubbedDocument()!;

        scrubbed.Inputs.Should().BeEquivalentTo(original.Inputs);
        scrubbed.Provenance.Should().BeEquivalentTo(original.Provenance);
        scrubbed.Slices.Should().BeEquivalentTo(original.Slices);
        scrubbed.Degradations.Should().BeEquivalentTo(original.Degradations);
        scrubbed.Report.Requirements.Should().BeEquivalentTo(original.Report.Requirements);
        scrubbed.Report.Notes.Should().BeEquivalentTo(original.Report.Notes);
        scrubbed.Report.Degraded.Should().Be(original.Report.Degraded);
        scrubbed.Report.Recommendation!.ExpertId.Should().Be(Erased,
            "the id stays: restricted-processing reference, not anonymised data");
        scrubbed.Report.Candidates.Should().HaveCount(2, "no candidate is dropped from the record");
    }

    [Fact]
    public void A_document_naming_nobody_erased_is_returned_untouched()
    {
        var json = Document().Serialize();

        HandoffPackageScrub.Remove(json, Guid.NewGuid()).Should().BeSameAs(json);
    }

    [Fact]
    public void An_unreadable_column_is_left_exactly_as_it_was()
    {
        HandoffPackageScrub.Remove("not json at all", Erased).Should().Be("not json at all");
        HandoffPackageScrub.Remove(null, Erased).Should().BeNull();
        HandoffPackageScrub.Remove("   ", Erased).Should().Be("   ");
    }

    /// <summary>
    /// The scrub names the paths it clears; this walks the real serialized document and requires
    /// each one to exist in it. A field renamed in these records would otherwise leave the scrub
    /// addressing a path that is no longer there — silently clearing nothing.
    /// </summary>
    [Fact]
    public void Every_path_the_scrub_claims_to_clear_exists_in_a_real_document()
    {
        var json = Document().Serialize();

        foreach (var path in HandoffPackageScrub.ScrubbedPaths)
        {
            var leaf = path.Split('.').Last();
            json.Should().Contain($"\"{leaf}\":",
                $"the scrub addresses '{path}', so a real document has to carry it");
        }
    }

    private static StaffingHandoffDocument? ScrubbedDocument() =>
        StaffingHandoffDocument.TryDeserialize(
            HandoffPackageScrub.Remove(Document().Serialize(), Erased));

    private static StaffingHandoffDocument Document()
    {
        var report = new StaffingReport(
            Requirements: ["React", "Payments"],
            Candidates:
            [
                new StaffingCandidate(
                    Erased,
                    "Zarquon Erasable",
                    "Staff Engineer",
                    new StaffingShortlistDetail(
                        0.82,
                        new ShortlistCoverage(2, 2),
                        [new ShortlistRequirementItem("React", true, "Zarquon rebuilt the console.")]),
                    new StaffingMatchDetail(StaffingMatchStatus.Completed, 88, "strong",
                        "Zarquon has the experience.", null),
                    "Zarquon matched well."),
                new StaffingCandidate(
                    Bystander,
                    "Wren Ashgrove",
                    "Platform Engineer",
                    new StaffingShortlistDetail(
                        0.71,
                        new ShortlistCoverage(1, 2),
                        [new ShortlistRequirementItem("React", true, "Wren ran the platform.")]),
                    new StaffingMatchDetail(StaffingMatchStatus.Completed, 71, "fair",
                        "Wren scores 71.", null),
                    "Wren has shipped this twice."),
            ],
            Recommendation: new StaffingRecommendation(Erased, "Pick Zarquon Erasable."),
            Degraded: false,
            Notes: ["Ran clean."]);

        var package = new HandoffPackage(
            new Dictionary<string, string?> { ["jobDescription"] = "A job" },
            new RunProvenance(Requester, [], DateTimeOffset.UnixEpoch),
            [],
            []);

        return StaffingHandoffDocument.From(package, report);
    }
}
