using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Application.Claims;

/// <summary>
/// One row of the Service Manager's queue. Carries the claimant's address <em>and</em> the target
/// row's, because the whole decision in front of the approver is whether two strings being equal
/// means two people are the same person — and it does not.
/// </summary>
public sealed record ClaimQueueItemDto(
    Guid Id,
    Guid ClaimantUserId,
    string ClaimantEmail,
    Guid? ExpertId,
    string? ExpertName,
    string? ExpertEmail,
    int MatchCount,
    ClaimState State,
    DateTimeOffset CreatedAt);

/// <summary>
/// A freshly issued claim code and the row it binds. <see cref="Code"/> is the only time the
/// plaintext exists outside the Service Manager's hands — the database keeps a hash — so the screen
/// that receives this has to show it and say it will not be shown again.
/// </summary>
public sealed record ClaimCodeIssuedDto(Guid ExpertId, string Code);

/// <summary>
/// Who a roster row belongs to. Its own read rather than two more fields on
/// <c>ExpertDetailDto</c>: that projection is what <c>expert_get</c> hands every agent on every
/// model call, and ownership is a staff concern no agent acts on — two nulls riding along there
/// cost real tokens forever (the read-tool cost floors catch exactly this).
/// </summary>
public sealed record ExpertOwnershipDto(Guid ExpertId, Guid? OwnerUserId, string? OwnerEmail);

/// <summary>What registration did about the roster (P1T-184).</summary>
public enum RegistrationBinding
{
    /// <summary>Nothing matched, so a fresh row was created and is theirs immediately. Nobody
    /// else's data is involved, so there is nothing for a Service Manager to judge.</summary>
    OwnsNewRow = 1,

    /// <summary>Exactly one non-Draft row matched. A claim is waiting; the person owns nothing in
    /// the meantime and cannot tell the difference from owning nothing at all.</summary>
    ClaimPending = 2,

    /// <summary>More than one row matched, so no claim was created and a flag was raised. The way
    /// out is a claim code, which is proof in a way a matching address never is.</summary>
    AmbiguousRaised = 3
}

/// <summary>The outcome of binding a new account to the roster, for the caller to report.</summary>
public sealed record RegistrationBindingDto(RegistrationBinding Outcome, Guid? ExpertId, int MatchCount);
