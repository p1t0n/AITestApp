namespace ExpertToJob.Domain.Entities;

/// <summary>
/// One interval during which one embeddings provider was active on this deployment (EXP-66,
/// <c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 17).
///
/// <para><b>It exists so a past transfer can still be disclosed.</b> Art. 15(1)(c) asks who the
/// data has been disclosed to, not only who it is being disclosed to now, and a deployment that
/// moves embeddings from one provider to another would otherwise lose the fact that the earlier one
/// ever received anything the moment configuration changed. The switch is a configuration edit
/// nobody records anywhere else, so the history has to be written by the host that reads it.</para>
///
/// <para><b>Append-only, and it holds no personal data.</b> A provider name and two timestamps,
/// deployment-wide — no Expert id, no User id, nothing that names anybody. Which Experts a period
/// covers is worked out from their own record's creation date, so this table never needs to point
/// at a person and erasure never needs to reach it.</para>
/// </summary>
public class EmbeddingsProviderPeriod
{
    public Guid Id { get; set; }

    /// <summary>
    /// The provider's name as configuration spells it (<c>Gemini</c>, <c>AzureFoundry</c>). A
    /// string rather than the enum because this is a historical record: a member removed from the
    /// enum later must not make a period that really happened unreadable.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// When this provider became active, or <b>null meaning "since the beginning"</b> — the value
    /// the migration seeds for Gemini, which was the only provider from this service's first row.
    /// Stamping the migration's own run time instead would claim a start date nobody knows.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When it stopped being active. Null while it still is.</summary>
    public DateTimeOffset? EndedAt { get; set; }
}
