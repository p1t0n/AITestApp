using ExpertToJob.Application.Abstractions;
using ExpertToJob.Application.Common;
using ExpertToJob.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Application.Visibility;

/// <summary>
/// Whether the person is currently on the bench, and since when. What the pause control reads and
/// writes; <c>HiddenSince</c> is the answer the transparency view owes to "since when".
/// </summary>
public sealed record ExpertVisibilityDto(Guid ExpertId, bool Hidden, DateTimeOffset? HiddenSince);

/// <summary>
/// The pause control (P1T-185). Hiding is <b>the Expert's own act and nobody else's</b>: a Service
/// Manager who wants somebody off the bench deactivates their account instead, which is a different
/// mechanism with a different meaning, and staff cannot silently un-hide somebody who hid
/// themselves. Staff keep full write on CV <em>content</em> — this is an exit control, not content.
///
/// <para>Note what is missing from every signature: <b>there is no expert id</b>. The row acted on
/// is always the acting account's own, resolved from <c>OwnerUserId</c>. A rule enforced by a check
/// is a rule some later code path forgets to make; an API that cannot express "hide somebody else"
/// cannot be talked into it.</para>
/// </summary>
public interface IExpertVisibilityService
{
    /// <summary>Where the caller's own row stands. 404s when they own none — a person with no row
    /// has nothing to pause, and that is the same answer they get everywhere else (P1T-182).</summary>
    Task<ExpertVisibilityDto> MineAsync(Guid actingUserId, CancellationToken ct = default);

    /// <summary>Pauses the caller's own row. Idempotent: pausing an already-paused row keeps the
    /// original timestamp, because "since when" is a fact about the pause, not about the click.</summary>
    Task<ExpertVisibilityDto> HideMineAsync(Guid actingUserId, CancellationToken ct = default);

    /// <summary>Puts the caller's own row back on the bench. Costs nothing: the chunks and their
    /// embeddings were never deleted, so nothing is re-embedded and no quota is spent.</summary>
    Task<ExpertVisibilityDto> UnhideMineAsync(Guid actingUserId, CancellationToken ct = default);
}

public class ExpertVisibilityService(IAppDbContext db, TimeProvider clock) : IExpertVisibilityService
{
    public async Task<ExpertVisibilityDto> MineAsync(Guid actingUserId, CancellationToken ct = default) =>
        Project(await MyRowAsync(actingUserId, ct));

    public async Task<ExpertVisibilityDto> HideMineAsync(
        Guid actingUserId, CancellationToken ct = default)
    {
        var expert = await MyRowAsync(actingUserId, ct);
        if (expert.HiddenAt is null)
        {
            expert.HiddenAt = ToStorePrecision(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }

        return Project(expert);
    }

    public async Task<ExpertVisibilityDto> UnhideMineAsync(
        Guid actingUserId, CancellationToken ct = default)
    {
        var expert = await MyRowAsync(actingUserId, ct);
        if (expert.HiddenAt is not null)
        {
            expert.HiddenAt = null;
            await db.SaveChangesAsync(ct);
        }

        return Project(expert);
    }

    /// <summary>
    /// The caller's own row, by the owner column — never by an id they supplied. At most one row can
    /// name an account (the unique partial index says so), so the first match is the only one.
    /// </summary>
    private async Task<Expert> MyRowAsync(Guid actingUserId, CancellationToken ct) =>
        await db.Experts.FirstOrDefaultAsync(e => e.OwnerUserId == actingUserId, ct)
        ?? throw new NotFoundException(nameof(Expert), actingUserId);

    /// <summary>
    /// Truncates to the precision the store actually holds. Postgres <c>timestamptz</c> keeps
    /// microseconds and a .NET tick is 100ns, so writing the raw clock value means the response to
    /// the very first pause carries a "since when" no later read can ever return — the same field,
    /// two answers, one of them unreachable. Rounding on the way in makes the value this call
    /// returns the value every later read returns.
    /// </summary>
    private static DateTimeOffset ToStorePrecision(DateTimeOffset at) =>
        new(at.Ticks - (at.Ticks % TimeSpan.TicksPerMicrosecond), at.Offset);

    private static ExpertVisibilityDto Project(Expert e) =>
        new(e.Id, e.HiddenAt is not null, e.HiddenAt);
}
