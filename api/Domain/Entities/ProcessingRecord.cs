using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Domain.Entities;

/// <summary>
/// Why this service is allowed to hold one <see cref="Expert"/>'s data, as it stood at one moment
/// (P1T-183). Append-only: a basis is never edited, superseded, or rolled back — a change appends a
/// new row and the old one stays exactly as it was written. EDPB GL 05/2020 §123 makes the
/// <em>history</em> the artefact, because "we were on legitimate interest until March" is a fact
/// with consequences (an LI row has no Art. 22(2) route, so it was not scannable in that window)
/// and rewriting it would erase them.
///
/// <para>Append-only is enforced by the database, not by convention: a <c>BEFORE UPDATE</c> trigger
/// on the table raises. And <see cref="Basis"/> is not an independent field — it is a pure function
/// of <see cref="Origin"/> (<see cref="BasisFor"/>), with a CHECK constraint refusing any other
/// pairing, so there is no code path anywhere that can write a row onto the wrong ground.</para>
///
/// <para>This is deliberately <em>not</em> a consent record. Under Art. 6(1)(b) necessity does the
/// legal work; presenting a consent control where another basis applies is misleading (GL 05/2020),
/// so what the person actually does is acknowledge a versioned transparency notice — recorded here
/// as <see cref="NoticeVersion"/>.</para>
/// </summary>
public class ProcessingRecord
{
    public Guid Id { get; set; }

    /// <summary>The roster row this states the basis for. Cascade-deleted with it: erasure removes
    /// the record along with the data, which is a different act from rewriting it.</summary>
    public Guid ExpertId { get; set; }

    public Expert? Expert { get; set; }

    /// <summary>
    /// Position in this Expert's history, from 1. Unique per Expert. Timestamps alone cannot decide
    /// which record is in force — two rows written in the same tick would tie — and "which basis
    /// applies right now" is a question the Art. 22 route filter has to answer unambiguously.
    /// </summary>
    public int Sequence { get; set; }

    /// <summary>How the row came to be, at the moment this record was written.</summary>
    public ProcessingOrigin Origin { get; set; }

    /// <summary>The Art. 6(1) ground. Always <see cref="BasisFor"/> of <see cref="Origin"/>.</summary>
    public LawfulBasis Basis { get; set; }

    /// <summary>
    /// The exact transparency-notice version the person acknowledged, or null when nobody has —
    /// which is the ordinary state of a staff-created row. We never send email, so an Art. 14
    /// subject genuinely has not been reached; recording null says so rather than implying a
    /// notice was given.
    /// </summary>
    public string? NoticeVersion { get; set; }

    /// <summary>Why this row was appended, in plain words. Read back by the Art. 15 access view,
    /// so it is written for the data subject rather than for a developer.</summary>
    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>
    /// The one place a lawful basis is decided. Self-registration is a pre-contractual measure taken
    /// at the subject's own request (Art. 6(1)(b)); a row staff entered has only the company's own
    /// legitimate interest behind it (Art. 6(1)(f)). An unknown origin throws rather than falling
    /// back — a default here would be the global default this design exists to prevent.
    /// </summary>
    public static LawfulBasis BasisFor(ProcessingOrigin origin) => origin switch
    {
        ProcessingOrigin.SelfRegistered => LawfulBasis.ContractNecessity,
        ProcessingOrigin.StaffCreated => LawfulBasis.LegitimateInterest,
        _ => throw new ArgumentOutOfRangeException(
            nameof(origin), origin, "No lawful basis is defined for this origin."),
    };

    /// <summary>
    /// Builds a record. The only constructor callers are meant to use, because it is the only one
    /// that pairs basis with origin.
    /// </summary>
    public static ProcessingRecord For(
        Guid expertId,
        int sequence,
        ProcessingOrigin origin,
        string? noticeVersion,
        string reason,
        DateTimeOffset at) => new()
        {
            Id = Guid.NewGuid(),
            ExpertId = expertId,
            Sequence = sequence,
            Origin = origin,
            Basis = BasisFor(origin),
            NoticeVersion = noticeVersion,
            Reason = reason,
            RecordedAt = at,
        };
}
