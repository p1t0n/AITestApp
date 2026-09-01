using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Compliance;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Claims;

/// <summary>
/// How a <see cref="User"/> comes to own an <see cref="Expert"/> row (P1T-184), in a service where
/// email is never verified and no mail can ever be sent.
///
/// <para>Deliberately outside the own-row ownership scope every other roster service applies
/// (P1T-182), because this is the surface that <em>decides</em> ownership: a Service Manager acts on
/// rows nobody owns, and a claimant redeeming a code reaches a row precisely because they do not own
/// it yet. Authorization here is the endpoint policy above it and the claim code itself, and the
/// acting account is passed in rather than inferred, so a caller cannot decide on somebody's behalf
/// by accident.</para>
/// </summary>
public interface IClaimService
{
    /// <summary>
    /// Binds a brand-new account to the roster, or raises the request that would. Called from
    /// signup, once the account exists. Matching is case-insensitive and considers non-Draft rows
    /// only: a Draft is agent-staged and unvetted, so claiming one hands over a row no human has
    /// ever looked at.
    /// </summary>
    Task<RegistrationBindingDto> BindOnRegistrationAsync(
        Guid userId, string email, string? acknowledgedNoticeVersion, CancellationToken ct = default);

    /// <summary>Everything waiting on a Service Manager: open claims and raised flags, oldest first.</summary>
    Task<IReadOnlyList<ClaimQueueItemDto>> OpenAsync(CancellationToken ct = default);

    /// <summary>Who owns one row, and under what address. What the expert page reads to decide
    /// between offering a claim code and offering revocation.</summary>
    Task<ExpertOwnershipDto> OwnershipAsync(Guid expertId, CancellationToken ct = default);

    /// <summary>
    /// Binds the row to the claimant and appends the basis move to 6(1)(b) with the notice version
    /// the claimant acknowledged. Both happen in one transaction: a row that is owned but still
    /// recorded as legitimate interest is a compliance defect for as long as it lasts.
    /// </summary>
    Task<ClaimQueueItemDto> ApproveAsync(Guid claimId, Guid decidedByUserId, CancellationToken ct = default);

    /// <summary>Refuses an open claim, or dismisses a raised flag. The row is kept either way.</summary>
    Task<ClaimQueueItemDto> RejectAsync(Guid claimId, Guid decidedByUserId, CancellationToken ct = default);

    /// <summary>Issues a single-use code for a row. The plaintext comes back once and is not
    /// recoverable afterwards — only its hash is stored.</summary>
    Task<ClaimCodeIssuedDto> IssueCodeAsync(
        Guid expertId, Guid issuedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Spends a code and binds ownership with no approval step, because the code <em>is</em> the
    /// proof: a Service Manager handed it over in person. Returns the row now owned.
    /// </summary>
    Task<Guid> RedeemCodeAsync(string code, Guid claimantUserId, CancellationToken ct = default);

    /// <summary>
    /// Unbinds a row and appends a record returning it to legitimate interest — which also stops it
    /// being scanned, because LI carries no Art. 22(2) route. Appends rather than rewrites: the
    /// history has to keep showing the row <em>was</em> on 6(1)(b), because it was scannable then.
    /// </summary>
    Task RevokeAsync(Guid expertId, Guid revokedByUserId, CancellationToken ct = default);
}

public class ClaimService(
    IAppDbContext db, IOwnershipChangeRecorder records, TimeProvider clock) : IClaimService
{
    public async Task<RegistrationBindingDto> BindOnRegistrationAsync(
        Guid userId, string email, string? acknowledgedNoticeVersion, CancellationToken ct = default)
    {
        var normalised = Normalise(email);
        var now = clock.GetUtcNow();

        // Non-Draft only, case-insensitive. A Draft is agent-staged and unvetted, and it is also
        // exempt from the roster's email uniqueness — so drafts are invisible here twice over, and
        // an address that only matches drafts is treated as matching nothing.
        var matches = await db.Experts
            .Where(e => e.Status != ExpertStatus.Draft && e.Email.ToLower() == normalised)
            .Select(e => new { e.Id, e.OwnerUserId })
            .ToListAsync(ct);

        if (matches.Count == 0)
        {
            var expertId = await CreateOwnRowAsync(userId, email, acknowledgedNoticeVersion, now, ct);
            return new RegistrationBindingDto(RegistrationBinding.OwnsNewRow, expertId, 0);
        }

        var match = matches.Count == 1 && matches[0].OwnerUserId is null ? matches[0] : null;
        if (match is null)
        {
            // Two shapes, one answer: more than one row matched, or the single match already
            // belongs to somebody. Neither may be resolved automatically — auto-picking hands one
            // person another person's CV on a coin flip, and a second claimant on an owned row is
            // the takeover this whole design exists to prevent. No claim, a flag, and a human.
            db.PendingClaims.Add(Flag(userId, normalised, matches.Count, now));
            await db.SaveChangesAsync(ct);
            return new RegistrationBindingDto(RegistrationBinding.AmbiguousRaised, null, matches.Count);
        }

        db.PendingClaims.Add(new PendingClaim
        {
            Id = Guid.NewGuid(),
            ClaimantUserId = userId,
            ClaimantEmail = normalised,
            ExpertId = match.Id,
            MatchCount = 1,
            State = ClaimState.Pending,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);

        return new RegistrationBindingDto(RegistrationBinding.ClaimPending, match.Id, 1);
    }

    public async Task<IReadOnlyList<ClaimQueueItemDto>> OpenAsync(CancellationToken ct = default)
    {
        return await db.PendingClaims
            .AsNoTracking()
            .Where(c => c.State == ClaimState.Pending || c.State == ClaimState.Ambiguous)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new ClaimQueueItemDto(
                c.Id,
                c.ClaimantUserId,
                c.ClaimantEmail,
                c.ExpertId,
                c.Expert == null ? null : (c.Expert.FirstName + " " + c.Expert.LastName).Trim(),
                c.Expert == null ? null : c.Expert.Email,
                c.MatchCount,
                c.State,
                c.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<ExpertOwnershipDto> OwnershipAsync(Guid expertId, CancellationToken ct = default)
    {
        var owner = await db.Experts.AsNoTracking()
            .Where(e => e.Id == expertId)
            .Select(e => new { e.OwnerUserId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Expert), expertId);

        var email = owner.OwnerUserId is null
            ? null
            : await db.Users.AsNoTracking()
                .Where(u => u.Id == owner.OwnerUserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct);

        return new ExpertOwnershipDto(expertId, owner.OwnerUserId, email);
    }

    public async Task<ClaimQueueItemDto> ApproveAsync(
        Guid claimId, Guid decidedByUserId, CancellationToken ct = default)
    {
        var claim = await OpenClaimAsync(claimId, ct);
        if (claim.State != ClaimState.Pending || claim.ExpertId is null)
        {
            throw new ConflictException(
                "There is no row to bind: this is a raised flag, not a claim. Issue a claim code " +
                "for the right row instead, or dismiss it.");
        }

        var expert = await db.Experts.FirstOrDefaultAsync(e => e.Id == claim.ExpertId, ct)
            ?? throw new NotFoundException(nameof(Expert), claim.ExpertId);

        if (expert.OwnerUserId is not null)
        {
            throw new ConflictException(
                "This row already belongs to another account. Revoke that ownership first if the " +
                "claim is the correct one.");
        }

        var noticeVersion = await db.Users
            .Where(u => u.Id == claim.ClaimantUserId)
            .Select(u => u.AcknowledgedNoticeVersion)
            .FirstOrDefaultAsync(ct);

        expert.OwnerUserId = claim.ClaimantUserId;
        Resolve(claim, ClaimState.Approved, decidedByUserId);

        // Ownership and basis land together: SaveChanges inside the append flushes both in one
        // transaction, so the row is never owned-but-recorded-as-legitimate-interest.
        await records.AppendForOwnershipChangeAsync(
            expert.Id, ProcessingOrigin.SelfRegistered, noticeVersion,
            "Claim on this row approved by a Service Manager; the person registered and " +
            "acknowledged the transparency notice.",
            ct);

        return await ReadBackAsync(claim.Id, ct);
    }

    public async Task<ClaimQueueItemDto> RejectAsync(
        Guid claimId, Guid decidedByUserId, CancellationToken ct = default)
    {
        var claim = await OpenClaimAsync(claimId, ct);
        Resolve(claim, ClaimState.Rejected, decidedByUserId);
        await db.SaveChangesAsync(ct);
        return await ReadBackAsync(claim.Id, ct);
    }

    public async Task<ClaimCodeIssuedDto> IssueCodeAsync(
        Guid expertId, Guid issuedByUserId, CancellationToken ct = default)
    {
        var expert = await db.Experts.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == expertId, ct)
            ?? throw new NotFoundException(nameof(Expert), expertId);

        if (expert.Status == ExpertStatus.Draft)
        {
            throw new ConflictException(
                "A draft row is agent-staged and unvetted; promote it before handing it to anyone.");
        }

        if (expert.OwnerUserId is not null)
        {
            throw new ConflictException("This row already belongs to an account.");
        }

        var (code, plaintext) = ClaimCode.Issue(expertId, issuedByUserId, clock.GetUtcNow());
        db.ClaimCodes.Add(code);
        await db.SaveChangesAsync(ct);

        return new ClaimCodeIssuedDto(expertId, plaintext);
    }

    public async Task<Guid> RedeemCodeAsync(
        string code, Guid claimantUserId, CancellationToken ct = default)
    {
        var hash = ClaimCode.HashOf(code ?? string.Empty);
        var issued = await db.ClaimCodes.FirstOrDefaultAsync(c => c.CodeHash == hash, ct);

        // One refusal for "no such code" and for "already spent": a replay must not learn that the
        // code it is replaying was ever real.
        if (issued is null || issued.RedeemedAt is not null)
        {
            throw new ConflictException("This claim code is not valid. Ask for a new one.");
        }

        var expert = await db.Experts.FirstOrDefaultAsync(e => e.Id == issued.ExpertId, ct)
            ?? throw new ConflictException("This claim code is not valid. Ask for a new one.");

        if (expert.OwnerUserId is not null)
        {
            throw new ConflictException("This row already belongs to an account.");
        }

        if (await db.Experts.AnyAsync(e => e.OwnerUserId == claimantUserId, ct))
        {
            throw new ConflictException("This account already owns a roster row.");
        }

        var now = clock.GetUtcNow();
        issued.RedeemedAt = now;
        issued.RedeemedByUserId = claimantUserId;
        expert.OwnerUserId = claimantUserId;

        var claimant = await db.Users.FirstOrDefaultAsync(u => u.Id == claimantUserId, ct);

        // Redemption is a decision too, so it leaves the same trail an approval does — with itself
        // named as the decider, because no Service Manager looked at this one.
        var open = await db.PendingClaims
            .Where(c => c.ClaimantUserId == claimantUserId && c.State == ClaimState.Pending)
            .ToListAsync(ct);
        foreach (var superseded in open)
        {
            // Superseded, not adjudicated — the decider is whoever issued the code, because that is
            // the Service Manager whose act ended this request.
            Resolve(superseded, ClaimState.Rejected, issued.IssuedByUserId ?? claimantUserId);
        }

        db.PendingClaims.Add(new PendingClaim
        {
            Id = Guid.NewGuid(),
            ClaimantUserId = claimantUserId,
            ClaimantEmail = claimant?.Email ?? string.Empty,
            ExpertId = expert.Id,
            MatchCount = 1,
            State = ClaimState.Approved,
            CreatedAt = now,
            DecidedByUserId = issued.IssuedByUserId,
            DecidedAt = now,
        });

        await records.AppendForOwnershipChangeAsync(
            expert.Id, ProcessingOrigin.SelfRegistered, claimant?.AcknowledgedNoticeVersion,
            "Single-use claim code redeemed; a Service Manager handed it over out of band.",
            ct);

        return expert.Id;
    }

    public async Task RevokeAsync(Guid expertId, Guid revokedByUserId, CancellationToken ct = default)
    {
        var expert = await db.Experts.FirstOrDefaultAsync(e => e.Id == expertId, ct)
            ?? throw new NotFoundException(nameof(Expert), expertId);

        if (expert.OwnerUserId is null)
        {
            throw new ConflictException("This row belongs to nobody, so there is nothing to revoke.");
        }

        expert.OwnerUserId = null;

        await records.AppendForOwnershipChangeAsync(
            expert.Id, ProcessingOrigin.StaffCreated, null,
            "Ownership revoked by a Service Manager; the row returns to legitimate interest.",
            ct);
    }

    /// <summary>
    /// The row a self-registering person gets when nothing matched. Created Active and owned in the
    /// same graph as its first <see cref="ProcessingRecord"/> — <see cref="ProcessingOrigin.SelfRegistered"/>,
    /// because this row exists only because they asked to be considered for work, which is the
    /// pre-contractual measure Art. 6(1)(b) turns on.
    ///
    /// <para>Name fields are left empty rather than guessed from the address. A bench row is about a
    /// real person and inventing their name is worse than showing an incomplete row the person
    /// themselves fills in.</para>
    /// </summary>
    private async Task<Guid> CreateOwnRowAsync(
        Guid userId, string email, string? noticeVersion, DateTimeOffset now, CancellationToken ct)
    {
        var expert = new Expert
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId,
            Status = ExpertStatus.Active,
            Email = email.Trim(),
        };

        expert.ProcessingRecords.Add(ProcessingRecord.For(
            expert.Id, sequence: 1, ProcessingOrigin.SelfRegistered, noticeVersion,
            "Registered on this service and asked to be considered for work.", now));

        db.Experts.Add(expert);
        await db.SaveChangesAsync(ct);
        return expert.Id;
    }

    private static PendingClaim Flag(Guid userId, string email, int matchCount, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ClaimantUserId = userId,
        ClaimantEmail = email,
        ExpertId = null,
        MatchCount = matchCount,
        State = ClaimState.Ambiguous,
        CreatedAt = now,
    };

    private async Task<PendingClaim> OpenClaimAsync(Guid claimId, CancellationToken ct)
    {
        var claim = await db.PendingClaims.FirstOrDefaultAsync(c => c.Id == claimId, ct)
            ?? throw new NotFoundException(nameof(PendingClaim), claimId);

        if (claim.State is ClaimState.Approved or ClaimState.Rejected)
        {
            throw new ConflictException("This claim has already been decided.");
        }

        return claim;
    }

    private void Resolve(PendingClaim claim, ClaimState state, Guid decidedByUserId)
    {
        claim.State = state;
        claim.DecidedByUserId = decidedByUserId;
        claim.DecidedAt = clock.GetUtcNow();
    }

    private async Task<ClaimQueueItemDto> ReadBackAsync(Guid claimId, CancellationToken ct)
    {
        var c = await db.PendingClaims.AsNoTracking()
            .Include(x => x.Expert)
            .FirstOrDefaultAsync(x => x.Id == claimId, ct)
            ?? throw new NotFoundException(nameof(PendingClaim), claimId);

        return new ClaimQueueItemDto(
            c.Id, c.ClaimantUserId, c.ClaimantEmail, c.ExpertId,
            c.Expert is null ? null : $"{c.Expert.FirstName} {c.Expert.LastName}".Trim(),
            c.Expert?.Email, c.MatchCount, c.State, c.CreatedAt);
    }

    private static string Normalise(string email) => email.Trim().ToLowerInvariant();
}
