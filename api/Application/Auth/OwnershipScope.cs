using ExpertToJob.Domain.Entities;

namespace ExpertToJob.Application.Auth;

/// <summary>
/// How far a caller's reach over roster rows extends (P1T-182). Two states, and the second one is
/// the point: an Expert reaches exactly one <see cref="Expert"/> row — their own — and nothing else,
/// no matter which door they came through.
///
/// <para>This lives in the Application layer, applied by the services themselves, rather than as a
/// boundary authorization handler or an EF global query filter. A handler guards one door, and the
/// Web API and the MCP server share these services. A global filter would silently rewrite every
/// query in the system, including the agents' — a roster-wide search quietly returning one row is a
/// far worse failure than a service that forgets, because nothing would ever tell you.</para>
/// </summary>
public sealed class OwnershipScope
{
    private OwnershipScope(bool unrestricted, Guid? expertId)
    {
        IsUnrestricted = unrestricted;
        ExpertId = expertId;
    }

    /// <summary>The whole roster: Service Managers, and every MCP agent.</summary>
    public static OwnershipScope Unrestricted { get; } = new(true, null);

    /// <summary>
    /// One row, or none. <c>OwnedBy(null)</c> is a legitimate state, not an error — an Expert who
    /// has signed up but whose claim on a row is not approved yet owns nothing, and every own-row
    /// endpoint 404s for them uniformly. A pending claim is therefore structurally
    /// indistinguishable from no access at all, which is what stops the API from confirming that
    /// some row exists.
    /// </summary>
    public static OwnershipScope OwnedBy(Guid? expertId) => new(false, expertId);

    public bool IsUnrestricted { get; }

    /// <summary>The single row this caller owns; null when unrestricted, or when they own none.</summary>
    public Guid? ExpertId { get; }

    /// <summary>Whether this caller may see the given roster row. In-memory counterpart of the
    /// <c>Where</c> clauses the services apply when loading.</summary>
    public bool Allows(Guid expertId) => IsUnrestricted || ExpertId == expertId;

    /// <summary>
    /// The two values a query needs, so a service can name them as locals and keep its predicate
    /// readable — and translatable: <c>unrestricted || x.ExpertId == owned</c>.
    /// </summary>
    public void Deconstruct(out bool unrestricted, out Guid? owned)
    {
        unrestricted = IsUnrestricted;
        owned = ExpertId;
    }
}

/// <summary>
/// Resolves the current caller's <see cref="OwnershipScope"/>. Scoped per request: the Web host
/// reads the session's role and looks up the row the user owns; the MCP server answers
/// <see cref="OwnershipScope.Unrestricted"/> for every agent.
/// </summary>
public interface IOwnershipScopeProvider
{
    ValueTask<OwnershipScope> CurrentAsync(CancellationToken ct = default);
}

/// <summary>
/// Always the whole roster. Used by the MCP server — an agent acts on the roster as a whole, and its
/// authorization is the tool grants it was issued, not row ownership — and by tests that are not
/// about ownership.
/// </summary>
public sealed class UnrestrictedOwnershipScopeProvider : IOwnershipScopeProvider
{
    public ValueTask<OwnershipScope> CurrentAsync(CancellationToken ct = default) =>
        new(OwnershipScope.Unrestricted);
}
