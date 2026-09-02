namespace ExpertToJob.Domain.Entities;

/// <summary>
/// One Service Manager extracting one person's complete file on their behalf (P1T-187) — the
/// phoned-in request, since this service has no email to receive one by.
///
/// <para><b>This is a record about the staff member, not about the Expert.</b> The distinction is
/// the whole reason it is allowed to exist: a per-row log of who <em>viewed</em> whom was
/// deliberately rejected, because answering a disclosure duty by manufacturing a large new store of
/// personal data about access would then need its own disclosure, retention and erasure. Nothing
/// here records anybody merely looking; it records the one deliberate act of taking a copy of
/// somebody's whole record away.</para>
///
/// <para>A person exporting their own data writes no row at all.</para>
/// </summary>
public class DataExportRecord
{
    public Guid Id { get; set; }

    /// <summary>Whose file was taken. Cascades with them: after erasure there is no file to have
    /// taken, and the row would be a reference to a person we no longer hold.</summary>
    public Guid ExpertId { get; set; }

    public Expert? Expert { get; set; }

    /// <summary>The Service Manager who took it. Cleared rather than cascaded when their own
    /// account goes — the act still happened, and the ledger keeps the fact.</summary>
    public Guid? ExportedByUserId { get; set; }

    public DateTimeOffset ExportedAt { get; set; }
}
