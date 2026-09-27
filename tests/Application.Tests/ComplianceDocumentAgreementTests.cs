using ExpertToJob.Application.Compliance;
using FluentAssertions;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// The compliance documents and the strings a data subject actually reads, held to each other
/// (EXP-68, <c>manuals/adr-embeddings-provider-seam.md</c>).
///
/// <para><b>Why this is a test and not a convention.</b> The DPIA already states the rule in its
/// own prose — "an edit here that does not also move the code makes this document and the access
/// view disagree, which is the divergence an auditor finds" — and that rule survived EXP-21,
/// EXP-41 and EXP-64 as prose while the code moved underneath it three times. Nothing failed each
/// time, because a document that describes the wrong recipient is not a crash. A grep in a ticket's
/// acceptance criteria catches it once; this catches it on every push.</para>
///
/// <para><b>What it deliberately does not do</b> is assert that a manual is correct — no test can.
/// It asserts two narrower things that <em>are</em> mechanical: that no document still encodes the
/// superseded rule "embeddings go to Google whatever chat does", and that the recipient wording the
/// documents quote in bold is wording <see cref="Art15Disclosure"/> really produces.</para>
/// </summary>
public class ComplianceDocumentAgreementTests
{
    /// <summary>
    /// The claim the embeddings seam retired (chat ADR §2 decision 3). A document may still contain
    /// the phrase — a decision record has to be able to state the decision it is recording, and the
    /// embeddings ADR has to be able to name what it supersedes — but only where the same file also
    /// carries the supersession pointer, so a reader who lands on it is told within one file that it
    /// no longer holds.
    /// </summary>
    [Fact]
    public void No_document_says_embeddings_go_to_google_whatever_chat_does()
    {
        // Spelled in pieces so this file is not itself an offender the sweep has to exempt.
        var claim = string.Join(' ', "whatever", "chat", "does");
        var alsoTheClaim = string.Join(' ', "stay", "on", "Google");

        var offenders = TrackedText()
            .Where(f => f.Text.Contains(claim, StringComparison.OrdinalIgnoreCase)
                        || f.Text.Contains(alsoTheClaim, StringComparison.OrdinalIgnoreCase))
            // Either the file is the ADR that retired the claim, or it cites that ADR. A file
            // cannot cite itself, which is why the first half is by path.
            .Where(f => f.Relative != "manuals/adr-embeddings-provider-seam.md"
                        && !f.Text.Contains("adr-embeddings-provider-seam", StringComparison.Ordinal))
            .Select(f => f.Relative)
            .ToList();

        offenders.Should().BeEmpty(
            "embeddings follow Ai:Embeddings:Provider since EXP-67, and a file that still states "
            + "the old rule without pointing at the ADR that retired it reads as current");
    }

    /// <summary>
    /// The two facts the DPIA has to carry that no other document does: that a Gemini deployment is
    /// for development and demo data only (ADR §5 — Google's own terms, not our preference), and
    /// that Azure embeddings are EU-confined while Azure chat is not (ADR §7). Both are the reason
    /// residual risk R7 changed shape rather than closing, so a tidy that dropped either would
    /// leave R7 describing a system that no longer exists.
    /// </summary>
    [Theory]
    [InlineData("DataZoneStandard", "the residency claim rests on the quota tier, not on the region")]
    [InlineData("development and demo", "Google's terms put real people out of scope for Gemini")]
    [InlineData("55", "the abuse-log retention Google's terms state, researched in EXP-63")]
    [InlineData("Google Cloud EMEA", "the contracting entity an EEA operator would face")]
    public void The_dpia_records_what_the_embeddings_move_actually_changed(string fact, string why)
    {
        Read("manuals/dpia-expert-workspace.md").Should().Contain(fact, why);
    }

    /// <summary>
    /// The DPIA's recipients table quotes the disclosure's own wording in bold, on the stated
    /// promise that the two move together. Asserted against the code rather than against a second
    /// literal, so the table cannot drift into describing a recipient nobody is told about.
    /// </summary>
    [Theory]
    [InlineData(DisclosedChatProvider.Gemini, DisclosedEmbeddingsProvider.Gemini)]
    [InlineData(DisclosedChatProvider.AzureFoundry, DisclosedEmbeddingsProvider.AzureFoundry)]
    [InlineData(DisclosedChatProvider.AzureFoundry, DisclosedEmbeddingsProvider.Gemini)]
    [InlineData(DisclosedChatProvider.Gemini, DisclosedEmbeddingsProvider.AzureFoundry)]
    public void The_dpia_quotes_the_recipient_wording_the_code_produces(
        DisclosedChatProvider chat, DisclosedEmbeddingsProvider embeddings)
    {
        var dpia = Read("manuals/dpia-expert-workspace.md");

        foreach (var recipient in Art15Disclosure.RecipientsFor(chat, embeddings)
                     .Where(r => r.Recipient.Contains("Google") || r.Recipient.Contains("Microsoft")))
        {
            dpia.Should().Contain(recipient.Recipient,
                $"the recipients table covers {chat} chat with {embeddings} embeddings, and the "
                + "table's own rule is that an edit here that does not also move the code makes "
                + "this document and the access view disagree");
        }
    }

    /// <summary>
    /// The past-tense entry is the one a reader of the DPIA cannot derive from the present-tense
    /// table, and the one an auditor asks about first on a deployment that switched.
    /// </summary>
    [Fact]
    public void The_dpia_names_the_former_recipient_entry_and_the_table_behind_it()
    {
        var dpia = Read("manuals/dpia-expert-workspace.md");

        dpia.Should().Contain("Google (Gemini), formerly our embeddings provider",
            "the access view shows this entry to anybody whose record predates the switch");
        dpia.Should().Contain("EmbeddingsProviderPeriods",
            "the entry is derived from that table, and the DPIA has to say what the table holds");
    }

    /// <summary>
    /// EXP-55's standalone accuracy finding (P12), true before the embeddings move and after it:
    /// the transparency notice names no provider, so an assessment that leans on "disclosed in the
    /// notice" is leaning on something that is not there. Asserted as an absence because the fix is
    /// a deletion — the access view alone is what discloses it.
    /// </summary>
    [Fact]
    public void The_legitimate_interest_assessment_does_not_claim_the_notice_names_a_provider()
    {
        var lia = Read("manuals/legitimate-interest-assessment.md");

        lia.Should().NotContain("disclosed to the person in the notice",
            "TransparencyNotice.V20260901 names no provider and does not mention embeddings; the "
            + "access view is the only place a recipient is named");
        lia.Should().Contain("on the access view",
            "the disclosure that does happen still has to be credited, or the balancing test "
            + "understates what the person is told");
    }

    /// <summary>
    /// The chat ADR keeps its text — a decision record that may be rewritten records nothing — but a
    /// reader who opens it has to learn in the first screen that one of its decisions no longer
    /// holds.
    /// </summary>
    [Fact]
    public void The_chat_adr_carries_a_supersession_pointer_rather_than_a_rewrite()
    {
        var adr = Read("manuals/adr-chat-provider-seam.md");

        adr.Should().Contain("adr-embeddings-provider-seam.md",
            "the embeddings ADR supersedes this one in part (§8), and the pointer has to be here "
            + "too: a reader arriving at the chat ADR does not know the other one exists");
        adr.Should().Contain("Superseded in part",
            "stated as a status, not buried in a paragraph");
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    /// <summary>
    /// Every tracked document and source file the claim could hide in: the manuals, the two
    /// agent-facing files at the root, and the C# under <c>api/</c> whose XML comments restate the
    /// same rules. Discovered rather than listed, so a manual added later is swept the day it
    /// ships.
    /// </summary>
    private static IEnumerable<(string Relative, string Text)> TrackedText()
    {
        var root = RepoRoot();

        var files = Directory.EnumerateFiles(Path.Combine(root, "manuals"), "*.md")
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "api"), "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            .Append(Path.Combine(root, "README.md"))
            .Append(Path.Combine(root, "CLAUDE.md"));

        return files.Select(p =>
            (Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'),
             File.ReadAllText(p)));
    }

    /// <summary>Walks up from the test binary until the solution file appears — the tests run from
    /// <c>bin/</c>, and hard-coding a depth breaks the first time the layout moves.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpertToJob.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not find ExpertToJob.slnx above the test binary; the document sweep cannot run.");
    }
}
