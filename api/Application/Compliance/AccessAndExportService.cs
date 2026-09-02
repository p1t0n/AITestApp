using System.Text.Json;
using System.Text.Json.Nodes;
using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Cv;
using ExpertToJob.Application.Experts;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Compliance;

/// <summary>Whether taking a copy away is a right or something we simply do not mind giving.</summary>
public enum ExportEntitlement
{
    /// <summary>Art. 20 applies: the record is held on 6(1)(b), because the person registered or
    /// their claim was approved.</summary>
    Right = 1,

    /// <summary>Art. 20 does not apply to a legitimate-interest record. We give the same file
    /// anyway, and say what it is.</summary>
    Courtesy = 2
}

/// <summary>One automated assessment of this person, as they are entitled to read it.</summary>
public sealed record DerivedAssessmentDto(
    string Source,
    Guid SourceId,
    DateTimeOffset? At,
    int? Score,
    string? Band,
    string? Rationale,
    string? Digest,
    string? MatchAnswer);

/// <summary>
/// Everything software worked out <em>about</em> the person, rather than everything they told us.
/// Owed under Art. 15 (EDPB GL 01/2022 §§97–99) and excluded from the Art. 20 export, which is why
/// it is a separate shape rather than more fields on the record.
/// </summary>
public sealed record DerivedDataDto(
    IReadOnlyList<DerivedAssessmentDto> Assessments,
    string SearchIndexNote);

/// <summary>The Art. 15 access view: what we hold, why, who sees it, and what software decided.</summary>
public sealed record AccessViewDto(
    Guid ExpertId,
    ProcessingOrigin Origin,
    LawfulBasis Basis,
    string? Source,
    string? NoticeVersionAcknowledged,
    DateTimeOffset? PausedSince,
    ExportEntitlement Export,
    /// <summary>Which retention clock this record is on (P1T-188).</summary>
    RetentionClock RetentionClock,
    /// <summary>When this record will be deleted if nothing else happens, or null when nothing will
    /// delete it. Art. 15(1)(d) asks for the period; giving the person their own date is the form of
    /// it they can actually act on.</summary>
    DateTimeOffset? ExpiresAt,
    /// <summary>Whether the record is inside its last thirty days — what the banner renders on.
    /// Signing in to read the warning is itself activity, so for a claimed record the warning cures
    /// the thing it warns about.</summary>
    bool ExpiringSoon,
    IReadOnlyList<string> Purposes,
    IReadOnlyList<string> DataCategories,
    IReadOnlyList<RecipientCategory> Recipients,
    string Retention,
    string Art22Logic,
    IReadOnlyList<string> Rights,
    string ComplaintRight,
    CvDto Record,
    DerivedDataDto Derived,
    IReadOnlyList<ProcessingRecordDto> History);

/// <summary>
/// The Art. 20 portable copy. The same record, and <b>none of the derived data</b> — portability
/// covers what the person provided, not what we worked out about them.
/// </summary>
public sealed record DataExportDto(
    Guid ExpertId,
    DateTimeOffset ExportedAt,
    ExportEntitlement Entitlement,
    string EntitlementNote,
    ProcessingOrigin Origin,
    LawfulBasis Basis,
    string? NoticeVersionAcknowledged,
    CvDto Record,
    IReadOnlyList<ProcessingRecordDto> History);

/// <summary>
/// The two transparency surfaces (P1T-187), which are <b>two surfaces on purpose</b>: Art. 15 access
/// covers data derived about somebody and Art. 20 portability does not, so the export has to filter
/// out exactly what the access view has to include. Building one and labelling it twice would get
/// one of the two wrong.
///
/// <para>Ownership-scoped like every other roster service (P1T-182): an Expert reaches their own
/// record and a Service Manager reaches any, which is what makes the on-behalf export a matter of
/// who is asking rather than a second code path.</para>
/// </summary>
public interface IAccessAndExportService
{
    /// <summary>Everything Art. 15 owes about one record, derived data included.</summary>
    Task<AccessViewDto> AccessAsync(Guid expertId, CancellationToken ct = default);

    /// <summary>The portable copy. Writes nothing: somebody reading their own data is not an event
    /// worth a row, and recording it would be the read log this design deliberately refused.</summary>
    Task<DataExportDto> ExportAsync(Guid expertId, CancellationToken ct = default);

    /// <summary>
    /// The same copy, taken by a Service Manager for somebody who asked out of band — phoned in,
    /// since there is no email to ask by. <b>This act writes its own record</b>, because a staff
    /// member extracting a person's complete file should leave a trace. The row is about the staff
    /// member, not about the Expert.
    /// </summary>
    Task<DataExportDto> ExportOnBehalfAsync(
        Guid expertId, Guid staffUserId, CancellationToken ct = default);
}

public class AccessAndExportService(
    IAppDbContext db,
    IExpertService experts,
    ICvService cv,
    IProcessingRecordService records,
    TimeProvider clock) : IAccessAndExportService
{
    public async Task<AccessViewDto> AccessAsync(Guid expertId, CancellationToken ct = default)
    {
        // Through the roster service, so the ownership scope decides reachability exactly as it does
        // everywhere else: somebody else's record is 404, indistinguishable from one that is not there.
        var detail = await experts.GetAsync(expertId, ct);
        var history = await records.HistoryAsync(expertId, ct);
        var current = await records.CurrentAsync(expertId, ct);

        // The same function the sweep runs, so the date somebody is shown is the date their record
        // actually goes — one answer, not a description and a behaviour that drift apart.
        var retention = RetentionPolicy.For(
            detail.Email,
            isClaimed: await db.Experts.AnyAsync(e => e.Id == expertId && e.OwnerUserId != null, ct),
            collectedAt: history[0].RecordedAt,
            lastActivityAt: detail.LastActivityAt);

        return new AccessViewDto(
            expertId,
            current.Origin,
            current.Basis,
            Art15Disclosure.SourceFor(current.Origin),
            history.Select(r => r.NoticeVersion).LastOrDefault(v => v is not null),
            detail.HiddenAt,
            EntitlementFor(current.Basis),
            retention.Clock,
            retention.ExpiresAt,
            retention.IsInFinalWarningAt(clock.GetUtcNow()),
            Art15Disclosure.Purposes,
            Art15Disclosure.DataCategories,
            Art15Disclosure.Recipients,
            $"{Art15Disclosure.Retention} {RetentionPolicy.DescriptionFor(retention.Clock)}",
            Art15Disclosure.Art22Logic,
            Art15Disclosure.Rights,
            Art15Disclosure.ComplaintRight,
            await cv.BuildAsync(expertId, ct),
            await DerivedAsync(expertId, ct),
            history);
    }

    public Task<DataExportDto> ExportAsync(Guid expertId, CancellationToken ct = default) =>
        BuildExportAsync(expertId, staffUserId: null, ct);

    public Task<DataExportDto> ExportOnBehalfAsync(
        Guid expertId, Guid staffUserId, CancellationToken ct = default) =>
        BuildExportAsync(expertId, staffUserId, ct);

    private async Task<DataExportDto> BuildExportAsync(
        Guid expertId, Guid? staffUserId, CancellationToken ct)
    {
        var record = await cv.BuildAsync(expertId, ct);
        var history = await records.HistoryAsync(expertId, ct);
        var current = await records.CurrentAsync(expertId, ct);
        var entitlement = EntitlementFor(current.Basis);
        var now = clock.GetUtcNow();

        if (staffUserId is { } exportedBy)
        {
            // A record about the Service Manager, not about the Expert: one deliberate staff act of
            // extracting somebody's complete file, which should leave a trace. This is not the
            // per-row read log that was rejected — nothing here records anybody merely looking.
            db.DataExportRecords.Add(new DataExportRecord
            {
                Id = Guid.NewGuid(),
                ExpertId = expertId,
                ExportedByUserId = exportedBy,
                ExportedAt = now,
            });
            await db.SaveChangesAsync(ct);
        }

        return new DataExportDto(
            expertId,
            now,
            entitlement,
            EntitlementNote(entitlement),
            current.Origin,
            current.Basis,
            history.Select(r => r.NoticeVersion).LastOrDefault(v => v is not null),
            record,
            history);
    }

    /// <summary>
    /// Art. 20 is owed to a 6(1)(b) record and to nobody else. We hand over the same file either
    /// way and say which it is — building a basis check whose only job is to <em>deny</em> a file we
    /// are happy to give would be worse than useless, and the label stays truthful by itself when an
    /// approved claim moves a record from legitimate interest to contract necessity.
    /// </summary>
    private static ExportEntitlement EntitlementFor(LawfulBasis basis) =>
        basis == LawfulBasis.ContractNecessity ? ExportEntitlement.Right : ExportEntitlement.Courtesy;

    private static string EntitlementNote(ExportEntitlement entitlement) =>
        entitlement == ExportEntitlement.Right
            ? "You registered yourself, so this copy is yours by right under Art. 20 GDPR "
              + "(data portability)."
            : "Your record was created by a Service Manager rather than by you, so Art. 20 "
              + "portability does not apply to it. We are giving you the same copy anyway, as a "
              + "courtesy rather than as a right.";

    /// <summary>
    /// Everything software concluded about this person. Read straight from the stores the
    /// declaration names, so a scan or a proposal that holds a rationale is reachable by the person
    /// it was written about.
    /// </summary>
    private async Task<DerivedDataDto> DerivedAsync(Guid expertId, CancellationToken ct)
    {
        var scans = await db.ScoringJobCandidates
            .AsNoTracking()
            .Where(c => c.ExpertId == expertId)
            .Select(c => new DerivedAssessmentDto(
                "Roster scan", c.JobId, null, c.Score, c.Band, c.Rationale, c.Digest, null))
            .ToListAsync(ct);

        var proposals = await db.StaffingProposalCandidates
            .AsNoTracking()
            .Where(c => c.ExpertId == expertId)
            .Join(db.StaffingProposals, c => c.ProposalId, p => p.Id, (c, p) => new { c, p })
            .Select(x => new
            {
                x.p.Id,
                x.p.CreatedAt,
                x.c.MatchScore,
                x.c.MatchBand,
                x.c.Rationale,
                x.p.PackageJson,
            })
            .ToListAsync(ct);

        var assessments = scans
            .Concat(proposals.Select(p => new DerivedAssessmentDto(
                "Staffing proposal", p.Id, p.CreatedAt, p.MatchScore, p.MatchBand, p.Rationale,
                null, MatchAnswerFor(p.PackageJson, expertId))))
            .ToList();

        return new DerivedDataDto(
            assessments,
            "Your summary and each of your roles are also held as numeric representations "
            + "(embeddings) produced by Google's Gemini models, so that a search for a capability "
            + "can find your record. They are derived from the text above and hold nothing you have "
            + "not already read here.");
    }

    /// <summary>
    /// The one thing a person is owed that lives inside the handoff document rather than in a
    /// column: the model's written answer about them. Read by path for the same reason the erasure
    /// scrub writes by path — those record types live in the Agents host, and a second copy of them
    /// here is the drift the shared declaration exists to prevent (P1T-186).
    /// </summary>
    private static string? MatchAnswerFor(string? packageJson, Guid expertId)
    {
        if (string.IsNullOrWhiteSpace(packageJson))
        {
            return null;
        }

        try
        {
            var candidates = JsonNode.Parse(packageJson)?["report"]?["candidates"] as JsonArray;
            var mine = candidates?
                .OfType<JsonObject>()
                .FirstOrDefault(c => c["expertId"] is JsonValue id
                                     && id.TryGetValue<string>(out var value)
                                     && string.Equals(value, expertId.ToString(), StringComparison.OrdinalIgnoreCase));

            return mine?["match"]?["answer"]?.GetValue<string?>();
        }
        catch (JsonException)
        {
            // An unreadable document tells this person nothing; it is not a reason to fail the page
            // that shows them everything else.
            return null;
        }
    }
}
